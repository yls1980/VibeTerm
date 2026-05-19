using System.Windows;
using VibeTerm.Services;
using VibeTerm.Settings;

namespace VibeTerm;

public partial class App : Application
{
    public static AppSettings     Settings        { get; private set; } = new();
    public static SettingsService SettingsService { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Settings = SettingsService.Load();

        // Fire-and-forget: download xterm.js assets if not already cached locally.
        // terminal.html falls back to CDN automatically while the download runs.
        _ = XtermAssets.EnsureAssetsAsync();

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Unhandled error:\n{args.Exception.Message}",
                "VibeTerm Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsService.Save(Settings);
        base.OnExit(e);
    }
}
