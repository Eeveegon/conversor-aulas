namespace ConversorAulas.Core;

public static class AudioFiles
{
    public static readonly string[] Extensions = [".m4a", ".aac", ".wav", ".mp3", ".flac", ".ogg", ".opus", ".wma", ".aiff", ".aif", ".amr", ".3gp", ".caf", ".webm"];

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsGeneratedCopy(string path) => new[] { ".opus", ".m4a" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
        && System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @" - comprimido(?: \(\d+\))?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static string FormatSize(long bytes) => bytes >= 1_000_000_000 ? $"{bytes / 1_000_000_000d:0.##} GB"
        : bytes >= 1_000_000 ? $"{bytes / 1_000_000d:0.##} MB" : $"{bytes / 1_000d:0.##} KB";
}
