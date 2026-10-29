using System.Text.Json;

namespace ConversorAulas.Core;

public sealed record ConversionPreferences(AudioOutputFormat OutputFormat = AudioOutputFormat.Opus, int OpusSampleRate = 48000, int AacSampleRate = 44100)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ConversorAulas", "preferences.json");

    public static ConversionPreferences Load(string path)
    {
        try
        {
            var value = JsonSerializer.Deserialize<ConversionPreferences>(File.ReadAllText(path));
            return value is not null && Enum.IsDefined(value.OutputFormat) && value.OpusSampleRate is 48000 or 24000 or 16000
                && value.AacSampleRate is 44100 or 48000 or 24000 or 16000
                ? value : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }
}
