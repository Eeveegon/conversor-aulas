using System.Collections.ObjectModel;
using ConversorAulas.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace ConversorAulas.App;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<ConversionItem> items = [];
    private readonly AudioConverter converter = new(System.IO.Path.Combine(AppContext.BaseDirectory, "ffmpeg"));
    private CancellationTokenSource? conversionCancellation;
    private bool busy;
    private bool adding;
    private bool preferencesReady;

    public MainWindow()
    {
        InitializeComponent();
        var preferences = ConversionPreferences.Load(ConversionPreferences.DefaultPath);
        FormatBox.SelectedIndex = preferences.OutputFormat == AudioOutputFormat.M4a ? 1 : 0;
        SampleRateBox.SelectedIndex = preferences.OpusSampleRate switch { 24000 => 1, 16000 => 2, _ => 0 };
        AacSampleRateBox.SelectedIndex = preferences.AacSampleRate switch { 48000 => 1, 24000 => 2, 16000 => 3, _ => 0 };
        preferencesReady = true;
        UpdateOutputProfile();
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1040, 780));
        Root.ActualThemeChanged += (_, _) => UpdateTitleBarTheme();
        Root.Loaded += (_, _) => UpdateTitleBarTheme();
        FileList.ItemsSource = items;
        AppWindow.Closing += (_, args) =>
        {
            if (busy || adding)
            {
                args.Cancel = true;
                ShowNotice("Aguarda o fim da operação ou cancela a conversão antes de fechar.", InfoBarSeverity.Informational);
            }
        };
        try { converter.EnsureAvailable(); }
        catch (Exception ex) { ShowNotice(ex.Message, InfoBarSeverity.Error); }
    }

    private void UpdateTitleBarTheme()
    {
        if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()) return;
        var dark = Root.ActualTheme == ElementTheme.Dark;
        var background = Windows.UI.Color.FromArgb(255, dark ? (byte)32 : (byte)243, dark ? (byte)32 : (byte)243, dark ? (byte)32 : (byte)243);
        var foreground = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        var muted = dark ? Windows.UI.Color.FromArgb(255, 160, 160, 160) : Windows.UI.Color.FromArgb(255, 96, 96, 96);
        var hover = dark ? Windows.UI.Color.FromArgb(255, 55, 55, 55) : Windows.UI.Color.FromArgb(255, 225, 225, 225);
        var titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = titleBar.InactiveBackgroundColor = titleBar.ButtonBackgroundColor = titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ForegroundColor = titleBar.ButtonForegroundColor = foreground;
        titleBar.InactiveForegroundColor = titleBar.ButtonInactiveForegroundColor = muted;
        titleBar.ButtonHoverBackgroundColor = titleBar.ButtonPressedBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = titleBar.ButtonPressedForegroundColor = foreground;
    }

    private static int ReadSampleRate(ComboBox? box, int fallback) => box?.SelectedItem is ComboBoxItem { Tag: string value } && int.TryParse(value, out var rate) ? rate : fallback;
    private int SelectedSampleRate => SelectedOutputFormat == AudioOutputFormat.M4a ? ReadSampleRate(AacSampleRateBox, 44100) : ReadSampleRate(SampleRateBox, 48000);
    private AudioOutputFormat SelectedOutputFormat => FormatBox?.SelectedIndex == 1 ? AudioOutputFormat.M4a : AudioOutputFormat.Opus;

    private void Format_Changed(object sender, SelectionChangedEventArgs e) => SavePreferences();
    private void SampleRate_Changed(object sender, SelectionChangedEventArgs e) => SavePreferences();

    private void SavePreferences()
    {
        if (!preferencesReady) return;
        UpdateOutputProfile();
        try { new ConversionPreferences(SelectedOutputFormat, ReadSampleRate(SampleRateBox, 48000), ReadSampleRate(AacSampleRateBox, 44100)).Save(ConversionPreferences.DefaultPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice("A opção foi selecionada, mas não foi possível guardá-la para a próxima utilização.", InfoBarSeverity.Warning);
        }
    }

    private void UpdateOutputProfile()
    {
        var isM4a = SelectedOutputFormat == AudioOutputFormat.M4a;
        var frequency = (SelectedSampleRate / 1000d).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("pt-PT"));
        OutputFormatText.Text = isM4a ? $"Formato de saída: AAC (.m4a) · 64 kbps · {frequency} kHz · Mono"
            : $"Formato de saída: Opus (.opus) · 64 kbps · {SelectedSampleRate / 1000} kHz";
        SampleRateBox.Visibility = isM4a ? Visibility.Collapsed : Visibility.Visible;
        AacSampleRateBox.Visibility = isM4a ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            foreach (var extension in AudioFiles.Extensions) picker.FileTypeFilter.Add(extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var files = await picker.PickMultipleFilesAsync();
            await AddPathsAsync(files.Select(file => file.Path));
        }
        catch (Exception ex) { ShowNotice(ex.Message, InfoBarSeverity.Error); }
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) await AddPathsAsync([folder.Path]);
        }
        catch (Exception ex) { ShowNotice(ex.Message, InfoBarSeverity.Error); }
    }

    private async Task AddPathsAsync(IEnumerable<string> paths)
    {
        if (busy || adding) return;
        adding = true;
        UpdateControls();
        var recursive = SubfoldersCheck.IsChecked == true;
        var inputs = paths.ToArray();
        try
        {
            var candidates = await Task.Run(() =>
            {
                var found = new List<string>();
                foreach (var path in inputs)
                {
                    if (Directory.Exists(path))
                    {
                        found.AddRange(Directory.EnumerateFiles(path, "*", new EnumerationOptions
                        {
                            RecurseSubdirectories = recursive, IgnoreInaccessible = true,
                            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden
                        }).Where(file => AudioFiles.IsSupported(file) && !AudioFiles.IsGeneratedCopy(file)));
                    }
                    else if (AudioFiles.IsSupported(path)) found.Add(path);
                }
                return found.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            });
            var existing = items.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var errors = 0;
            var count = 0;
            foreach (var path in candidates)
            {
                if (!existing.Add(path)) continue;
                try { items.Add(new ConversionItem(path)); count++; }
                catch (IOException) { errors++; }
                catch (UnauthorizedAccessException) { errors++; }
            }
            SummaryText.Text = $"{items.Count} ficheiro(s) · {AudioFiles.FormatSize(items.Sum(item => item.Bytes))}";
            if (errors > 0) ShowNotice($"{errors} ficheiro(s) não puderam ser lidos.", InfoBarSeverity.Warning);
            else if (count == 0 && inputs.Length > 0) ShowNotice("Não foram encontrados novos ficheiros de áudio. As cópias já criadas são ignoradas na pesquisa de pastas.", InfoBarSeverity.Informational);
            else Notice.IsOpen = false;
        }
        finally { adding = false; UpdateControls(); }
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        if (busy || adding) return;
        try { converter.EnsureAvailable(); }
        catch (Exception ex) { ShowNotice(ex.Message, InfoBarSeverity.Error); return; }
        var pending = items.Where(item => !item.Completed).ToArray();
        if (pending.Length == 0) return;
        var sampleRate = SelectedSampleRate;
        var outputFormat = SelectedOutputFormat;
        busy = true;
        using var cancellation = new CancellationTokenSource();
        conversionCancellation = cancellation;
        Notice.IsOpen = false;
        TotalProgress.Value = 0;
        UpdateControls();
        var completed = 0;
        var failed = 0;
        long savedBytes = 0;
        try
        {
            for (var index = 0; index < pending.Length; index++)
            {
                if (cancellation.IsCancellationRequested) break;
                var item = pending[index];
                var currentIndex = index;
                item.Status = "A converter…";
                item.Progress = 0;
                SummaryText.Text = $"A converter {index + 1} de {pending.Length}: {item.Name}";
                FileList.ScrollIntoView(item);
                try
                {
                    var progress = new Progress<double>(value =>
                    {
                        // Ignore queued notifications from a conversion that has already finished.
                        if (item.Completed || !busy) return;
                        item.Progress = value;
                        TotalProgress.Value = (currentIndex + value / 100) / pending.Length * 100;
                    });
                    var result = await converter.ConvertAsync(item.Path, progress, cancellation.Token, sampleRate, outputFormat);
                    item.Completed = true;
                    item.Progress = 100;
                    var reduction = 100d * (1 - (double)result.OutputBytes / result.OriginalBytes);
                    item.Status = $"Concluído · {AudioFiles.FormatSize(result.OutputBytes)} · "
                        + (reduction >= 0 ? $"menos {reduction:0}%" : "o original já era mais pequeno")
                        + $" · {System.IO.Path.GetFileName(result.OutputPath)}";
                    completed++;
                    savedBytes += result.OriginalBytes - result.OutputBytes;
                }
                catch (OperationCanceledException) { item.Status = "Cancelado · podes voltar a converter"; item.Progress = 0; break; }
                catch (Exception ex) { item.Status = $"Erro: {ex.Message}"; item.Progress = 0; failed++; }
                TotalProgress.Value = (index + 1d) / pending.Length * 100;
            }
            SummaryText.Text = $"{completed} convertido(s) · {failed} erro(s)" + (cancellation.IsCancellationRequested ? " · operação cancelada" : "")
                + (savedBytes > 0 ? $" · cópias com menos {AudioFiles.FormatSize(savedBytes)}" : "");
            ShowNotice(cancellation.IsCancellationRequested ? "Conversão interrompida. As cópias já concluídas foram mantidas."
                : failed > 0 ? "Alguns ficheiros não foram convertidos. Consulta os detalhes na lista e volta a tentar."
                : "Tudo pronto! As cópias estão nas pastas dos originais.", failed > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational);
        }
        finally { busy = false; conversionCancellation = null; UpdateControls(); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        conversionCancellation?.Cancel();
        SummaryText.Text = "A cancelar…";
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (busy || adding) return;
        items.Clear();
        SummaryText.Text = "Adiciona ficheiros ou uma pasta para começar.";
        Notice.IsOpen = false;
        TotalProgress.Value = 0;
        UpdateControls();
    }

    private void Root_DragOver(object sender, DragEventArgs e) => e.AcceptedOperation = !busy && !adding && e.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None;

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (busy || adding || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        try { await AddPathsAsync((await e.DataView.GetStorageItemsAsync()).Select(item => item.Path)); }
        catch (Exception ex) { ShowNotice(ex.Message, InfoBarSeverity.Error); }
    }

    private void UpdateControls()
    {
        FilesButton.IsEnabled = FolderButton.IsEnabled = SubfoldersCheck.IsEnabled = !busy && !adding;
        SampleRateBox.IsEnabled = !busy && !adding;
        AacSampleRateBox.IsEnabled = !busy && !adding;
        FormatBox.IsEnabled = !busy && !adding;
        ClearButton.IsEnabled = !busy && !adding && items.Count > 0;
        ConvertButton.IsEnabled = !busy && !adding && items.Any(item => !item.Completed);
        CancelButton.Visibility = TotalProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = busy;
        EmptyPanel.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FileList.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowNotice(string message, InfoBarSeverity severity)
    {
        Notice.Message = message;
        Notice.Severity = severity;
        Notice.IsOpen = true;
    }
}
