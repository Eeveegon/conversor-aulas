using System.Diagnostics;
using System.Security.Cryptography;
using ConversorAulas.Core;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var tools = Path.Combine(root, "vendor", "ffmpeg");
var fixtures = Path.Combine(root, "artifacts", "integration", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixtures);
var converter = new AudioConverter(tools);
converter.EnsureAvailable();
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
    Console.WriteLine($"PASS: {message}");
}

foreach (var format in new[] { "wav", "m4a", "aac" })
{
    var input = Path.Combine(fixtures, $"Gravação d'aula com espaços.{format}");
    await Generate(input, 4);
    var hash = SHA256.HashData(await File.ReadAllBytesAsync(input));
    var result = await converter.ConvertAsync(input);
    var probe = await converter.ProbeAsync(result.OutputPath);
    Check(probe.Codec == "opus" && Math.Abs(probe.Duration - 4) < .2, $"{format}: saída Opus com duração preservada");
    var originalAfter = SHA256.HashData(await File.ReadAllBytesAsync(input));
    Check(hash.SequenceEqual(originalAfter), $"{format}: original inalterado");
    var outputHash = SHA256.HashData(await File.ReadAllBytesAsync(result.OutputPath));
    var second = await converter.ConvertAsync(input);
    var outputAfter = SHA256.HashData(await File.ReadAllBytesAsync(result.OutputPath));
    Check(second.OutputPath != result.OutputPath && outputHash.SequenceEqual(outputAfter), $"{format}: colisão de nomes não substitui cópias");
    if (format == "wav") Check(result.OutputBytes < result.OriginalBytes / 4, "WAV: redução significativa de tamanho");
}

foreach (var format in new[] { "wav", "m4a", "aac", "opus" })
{
    var input = Path.Combine(fixtures, $"AAC d'aula com espaços.{format}");
    await Generate(input, 10);
    var hash = SHA256.HashData(await File.ReadAllBytesAsync(input));
    var result = await converter.ConvertAsync(input, outputFormat: AudioOutputFormat.M4a);
    var probe = await converter.ProbeAsync(result.OutputPath);
    Check(Path.GetExtension(result.OutputPath) == ".m4a" && probe.Codec == "aac" && Math.Abs(probe.Duration - 10) < .2,
        $"{format}: saída M4A/AAC com duração preservada");
    Check(probe.SampleRate == 44100 && probe.Channels == 1, $"{format}: AAC 44,1 kHz mono");
    var originalAfter = SHA256.HashData(await File.ReadAllBytesAsync(input));
    Check(hash.SequenceEqual(originalAfter), $"{format} para AAC: original inalterado");
    var outputHash = SHA256.HashData(await File.ReadAllBytesAsync(result.OutputPath));
    var second = await converter.ConvertAsync(input, outputFormat: AudioOutputFormat.M4a);
    var outputAfter = SHA256.HashData(await File.ReadAllBytesAsync(result.OutputPath));
    Check(second.OutputPath != result.OutputPath && outputHash.SequenceEqual(outputAfter), $"{format} para AAC: colisão não substitui cópias");
    var details = await ProbeDetails(result.OutputPath);
    Check(details.Contains("profile=LC") && details.Contains("format_name=mov,mp4,m4a,3gp,3g2,mj2"), $"{format}: perfil AAC-LC e contentor MPEG-4");
    var bitrate = details.Split('\n').First(line => line.StartsWith("bit_rate="));
    Check(int.TryParse(bitrate[9..].Trim(), out var bits) && bits is > 54000 and < 74000, $"{format}: bitrate AAC próximo de 64 kbps");
}

var preferencesPath = Path.Combine(fixtures, "preferences", "preferences.json");
Check(ConversionPreferences.Load(preferencesPath) == new ConversionPreferences(), "Primeira utilização: Opus 48 kHz");
foreach (var selected in new[] { new ConversionPreferences(AudioOutputFormat.M4a, 24000, 16000), new ConversionPreferences(AudioOutputFormat.Opus, 16000, 48000) })
{
    selected.Save(preferencesPath);
    Check(ConversionPreferences.Load(preferencesPath) == selected, $"Preferência persistente: {selected.OutputFormat}");
}
await File.WriteAllTextAsync(preferencesPath, "invalid json");
Check(ConversionPreferences.Load(preferencesPath) == new ConversionPreferences(), "Preferências corrompidas: recuperação segura");
await File.WriteAllTextAsync(preferencesPath, "{\"OutputFormat\":99,\"OpusSampleRate\":44100}");
Check(ConversionPreferences.Load(preferencesPath) == new ConversionPreferences(), "Preferências desconhecidas: recuperação segura");
Check(AudioFiles.IsGeneratedCopy("Aula - comprimido.m4a") && AudioFiles.IsGeneratedCopy("Aula - comprimido (2).opus")
    && !AudioFiles.IsGeneratedCopy("Aula.m4a"), "Pesquisa de pastas ignora cópias em ambos os formatos");

var invalid = Path.Combine(fixtures, "inválido.wav");
var frequencyInput = Path.Combine(fixtures, "frequência.wav");
await Generate(frequencyInput, 4);
foreach (var rate in new[] { 44100, 48000, 24000, 16000 })
{
    var result = await converter.ConvertAsync(frequencyInput, sampleRate: rate, outputFormat: AudioOutputFormat.M4a);
    var encoded = await converter.ProbeAsync(result.OutputPath);
    Check(encoded.Codec == "aac" && encoded.SampleRate == rate && encoded.Channels == 1, $"AAC {rate} Hz: frequência selecionada e mono preservados");
}
try { await converter.ConvertAsync(frequencyInput, sampleRate: 12345, outputFormat: AudioOutputFormat.M4a); throw new Exception("Aceitou uma frequência incompatível."); }
catch (ArgumentOutOfRangeException) { Check(true, "AAC: frequência incompatível rejeitada antes da conversão"); }
foreach (var rate in new[] { 48000, 24000, 16000 })
{
    var result = await converter.ConvertAsync(frequencyInput, sampleRate: rate);
    var encoded = await File.ReadAllBytesAsync(result.OutputPath);
    var headerOffset = encoded.AsSpan().IndexOf("OpusHead"u8);
    Check(headerOffset >= 0 && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(headerOffset + 12, 4)) == rate,
        $"{rate} Hz: frequência de entrada registada no cabeçalho Opus");
}
try { await converter.ConvertAsync(frequencyInput, sampleRate: 44100); throw new Exception("Aceitou uma frequência incompatível."); }
catch (ArgumentOutOfRangeException) { Check(true, "44,1 kHz rejeitado antes da conversão Opus"); }

await File.WriteAllTextAsync(invalid, "This is not audio.");
foreach (var outputFormat in Enum.GetValues<AudioOutputFormat>())
{
    try { await converter.ConvertAsync(invalid, outputFormat: outputFormat); throw new Exception("O ficheiro inválido foi aceite."); }
    catch (InvalidDataException) { Check(!Directory.EnumerateFiles(fixtures, "inválido - comprimido.*").Any(), $"{outputFormat}: ficheiro inválido sem falsa cópia concluída"); }
}

var longInput = Path.Combine(fixtures, "cancelar.wav");
await Generate(longInput, 120);
foreach (var outputFormat in Enum.GetValues<AudioOutputFormat>())
{
    using var cancellation = new CancellationTokenSource();
    var progress = new CancelProgress(cancellation);
    try { await converter.ConvertAsync(longInput, progress, cancellation.Token, outputFormat: outputFormat); throw new Exception("Não cancelou."); }
    catch (OperationCanceledException) { Check(progress.Received, $"{outputFormat}: cancelamento durante a conversão real"); }
}
Check(!Directory.EnumerateFiles(fixtures, ".conversor-*").Any(), "Sem ficheiros temporários após erro/cancelamento");
Check(!Directory.EnumerateFiles(fixtures, "cancelar - comprimido.*").Any() && File.Exists(longInput), "Cancelamento preserva original e não publica cópia parcial");
Console.WriteLine($"{checks} verificações passaram. Ficheiros de teste: {fixtures}");

async Task Generate(string path, int seconds)
{
    var start = new ProcessStartInfo(Path.Combine(tools, "ffmpeg.exe")) { UseShellExecute = false, CreateNoWindow = true };
    foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", seconds.ToString(), path }) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new Exception("Não foi possível gerar o áudio de teste.");
}

async Task<string> ProbeDetails(string path)
{
    var start = new ProcessStartInfo(Path.Combine(tools, "ffprobe.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
    foreach (var argument in new[] { "-v", "error", "-show_entries", "stream=profile,bit_rate:format=format_name", "-of", "default=noprint_wrappers=1", path }) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    var output = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new Exception("FFprobe falhou.");
    return output;
}

sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<double>
{
    public bool Received { get; private set; }
    public void Report(double value) { Received = true; cancellation.Cancel(); }
}
