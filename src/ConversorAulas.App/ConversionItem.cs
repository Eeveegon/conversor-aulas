using System.ComponentModel;
using System.Runtime.CompilerServices;
using ConversorAulas.Core;

namespace ConversorAulas.App;

public sealed class ConversionItem(string path) : INotifyPropertyChanged
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
    public long Bytes { get; } = new FileInfo(path).Length;
    public string Size => AudioFiles.FormatSize(Bytes);
    public bool Completed { get; set; }
    private string status = "Pronto para converter";
    public string Status { get => status; set { status = value; Changed(); } }
    private double progress;
    public double Progress { get => progress; set { progress = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
