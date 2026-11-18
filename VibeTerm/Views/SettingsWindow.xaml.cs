using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using VibeTerm.Settings;

namespace VibeTerm.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        PopulateControls();
    }

    private void PopulateControls()
    {
        ShellPathBox.Text         = _settings.ShellPath;
        StartupDirBox.Text        = _settings.StartupDirectory;
        FontFamilyBox.Text        = _settings.FontFamily;
        FontSizeSlider.Value      = _settings.FontSize;
        EnableLoggingCheck.IsChecked = _settings.EnableLogging;
        MaskSensitiveCheck.IsChecked = _settings.MaskSensitiveInput;
        LogDirBox.Text            = _settings.LogDirectory;
        RetentionDaysBox.Text     = _settings.LogRetentionDays.ToString();
        EnablePanelApiCheck.IsChecked = _settings.EnablePanelApi;
    }

    private void BrowseShell_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select Shell Executable",
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            InitialDirectory = @"C:\Windows\System32"
        };
        if (dlg.ShowDialog() == true)
            ShellPathBox.Text = dlg.FileName;
    }

    private void OpenLogDir_Click(object sender, RoutedEventArgs e)
    {
        string dir = LogDirBox.Text;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        Process.Start("explorer.exe", dir);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShellPath        = ShellPathBox.Text.Trim();
        _settings.StartupDirectory = StartupDirBox.Text.Trim();
        _settings.FontFamily       = FontFamilyBox.Text.Trim();
        _settings.FontSize         = FontSizeSlider.Value;
        _settings.EnableLogging    = EnableLoggingCheck.IsChecked == true;
        _settings.MaskSensitiveInput = MaskSensitiveCheck.IsChecked == true;
        _settings.LogDirectory     = LogDirBox.Text.Trim();

        if (int.TryParse(RetentionDaysBox.Text, out int days))
            _settings.LogRetentionDays = days;

        _settings.EnablePanelApi = EnablePanelApiCheck.IsChecked == true;

        App.SettingsService.Save(_settings);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
