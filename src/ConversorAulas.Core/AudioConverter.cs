using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace ConversorAulas.Core;

public sealed record ConversionResult(string OutputPath, long OriginalBytes, long OutputBytes);
public sealed record AudioInfo(double Duration, string Codec);

public sealed class AudioConverter(string toolsDirectory)
{
    private readonly string ffmpeg = Path.Combine(toolsDirectory, "ffmpeg.exe");
    private readonly string ffprobe = Path.Combine(toolsDirectory, "ffprobe.exe");

    public void EnsureAvailable()
    {
        if (!File.Exists(ffmpeg) || !File.Exists(ffprobe))
            throw new FileNotFoundException("Não foi encontrado o FFmpeg incluído na aplicação. Volta a instalar a aplicação.");
    }

    public async Task<AudioInfo> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await RunAsync(ffprobe, ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=codec_name,duration:format=duration", "-of", "json", path], null, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
            throw new InvalidDataException("O ficheiro não contém uma faixa de áudio reconhecida.");
        var stream = streams[0];
        double duration = 0;
        if (root.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var value))
            double.TryParse(value.GetString(), CultureInfo.InvariantCulture, out duration);
        if (duration <= 0 && stream.TryGetProperty("duration", out var streamDuration))
            double.TryParse(streamDuration.GetString(), CultureInfo.InvariantCulture, out duration);
        return new AudioInfo(duration, stream.GetProperty("codec_name").GetString() ?? "");
    }

    public async Task<ConversionResult> ConvertAsync(string inputPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default, int sampleRate = 48000)
    {
        if (sampleRate is not (48000 or 24000 or 16000))
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Escolhe 48, 24 ou 16 kHz, frequências compatíveis com Opus.");
        EnsureAvailable();
        inputPath = Path.GetFullPath(inputPath);
        var originalBytes = new FileInfo(inputPath).Length;
        var info = await ProbeAsync(inputPath, cancellationToken);
        // A temporary file in the same folder allows an atomic final rename.
        var temporaryPath = Path.Combine(Path.GetDirectoryName(inputPath)!, $".conversor-{Guid.NewGuid():N}.tmp.opus");
        try
        {
            await RunAsync(ffmpeg,
                ["-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-i", inputPath,
                 "-map", "0:a:0", "-vn", "-map_metadata", "0", "-c:a", "libopus", "-b:a", "64k",
                 "-vbr", "on", "-ar", sampleRate.ToString(CultureInfo.InvariantCulture), "-ac", "2", "-application", "audio", "-f", "opus",
                 "-progress", "pipe:1", "-nostats", temporaryPath],
                line =>
                {
                    if (info.Duration > 0 && line.StartsWith("out_time_us=", StringComparison.Ordinal)
                        && long.TryParse(line.AsSpan(12), out var microseconds))
                        progress?.Report(Math.Clamp(microseconds / 1_000_000d / info.Duration * 100, 0, 99.5));
                }, cancellationToken);

            var outputInfo = await ProbeAsync(temporaryPath, cancellationToken);
            if (outputInfo.Codec != "opus" || new FileInfo(temporaryPath).Length == 0
                || (info.Duration > 0 && Math.Abs(outputInfo.Duration - info.Duration) > Math.Max(2, info.Duration * .01)))
                throw new InvalidDataException("A cópia convertida não passou a verificação de integridade.");
            cancellationToken.ThrowIfCancellationRequested();
            var outputPath = MoveToAvailableName(temporaryPath, inputPath);
            progress?.Report(100);
            return new ConversionResult(outputPath, originalBytes, new FileInfo(outputPath).Length);
        }
        finally
        {
            // Never delete originals or previously completed conversions.
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string MoveToAvailableName(string temporaryPath, string inputPath)
    {
        var stem = Path.Combine(Path.GetDirectoryName(inputPath)!, Path.GetFileNameWithoutExtension(inputPath) + " - comprimido");
        for (var number = 1; ; number++)
        {
            var candidate = stem + (number == 1 ? "" : $" ({number})") + ".opus";
            try { File.Move(temporaryPath, candidate, overwrite: false); return candidate; }
            catch (IOException) when (File.Exists(candidate)) { }
        }
    }

    private static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, Action<string>? onLine, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        // Drain both pipes concurrently; FFmpeg must never block on a full pipe.
        var errorTask = process.StandardError.ReadToEndAsync();
        var outputTask = ReadOutputAsync(process.StandardOutput, onLine);
        await process.WaitForExitAsync(CancellationToken.None);
        var error = await errorTask;
        var output = await outputTask;
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidDataException(string.IsNullOrWhiteSpace(error) ? "Não foi possível converter este ficheiro." : error.Trim());
        return output;
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, Action<string>? onLine)
    {
        var output = new System.Text.StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            if (onLine is null) output.AppendLine(line);
            else onLine(line);
        }
        return output.ToString();
    }
}
