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

var invalid = Path.Combine(fixtures, "inválido.wav");
var frequencyInput = Path.Combine(fixtures, "frequência.wav");
await Generate(frequencyInput, 4);
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
try { await converter.ConvertAsync(invalid); throw new Exception("O ficheiro inválido foi aceite."); }
catch (InvalidDataException) { Check(!File.Exists(Path.Combine(fixtures, "inválido - comprimido.opus")), "Ficheiro inválido: sem falsa cópia concluída"); }

var longInput = Path.Combine(fixtures, "cancelar.wav");
await Generate(longInput, 120);
using (var cancellation = new CancellationTokenSource())
{
    var progress = new CancelProgress(cancellation);
    try { await converter.ConvertAsync(longInput, progress, cancellation.Token); throw new Exception("Não cancelou."); }
    catch (OperationCanceledException) { Check(progress.Received, "Cancelamento durante a conversão real"); }
}
Check(!Directory.EnumerateFiles(fixtures, ".conversor-*").Any(), "Sem ficheiros temporários após erro/cancelamento");
Check(!File.Exists(Path.Combine(fixtures, "cancelar - comprimido.opus")) && File.Exists(longInput), "Cancelamento preserva original e não publica cópia parcial");
Console.WriteLine($"{checks} verificações passaram. Ficheiros de teste: {fixtures}");

async Task Generate(string path, int seconds)
{
    var start = new ProcessStartInfo(Path.Combine(tools, "ffmpeg.exe")) { UseShellExecute = false, CreateNoWindow = true };
    foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", seconds.ToString(), path }) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new Exception("Não foi possível gerar o áudio de teste.");
}

sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<double>
{
    public bool Received { get; private set; }
    public void Report(double value) { Received = true; cancellation.Cancel(); }
}
