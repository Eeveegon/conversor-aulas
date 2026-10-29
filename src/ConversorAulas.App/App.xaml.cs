using Microsoft.UI.Xaml;

namespace ConversorAulas.App;

public partial class App : Application
{
    private Window? window;
    public App()
    {
        UnhandledException += (_, args) => WriteDiagnostic(args.Message + Environment.NewLine + args.Exception);
        DebugSettings.XamlResourceReferenceFailed += (_, args) => WriteDiagnostic(args.Message);
        try { InitializeComponent(); }
        catch (Exception ex) { WriteDiagnostic(ex.ToString()); throw; }
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            window = new MainWindow();
            window.Activate();
        }
        catch (Exception ex) { WriteDiagnostic(ex.ToString()); throw; }
    }

    private static void WriteDiagnostic(string message)
    {
        var path = Environment.GetEnvironmentVariable("CONVERSOR_DIAGNOSTICS");
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
