using System;
using System.Collections.Generic;
using System.IO;

namespace VibeTerm.Settings;

public sealed class AppSettings
{
    // Shell
    public string ShellPath         { get; set; } = string.Empty; // empty = auto-detect
    public string StartupDirectory  { get; set; } = string.Empty; // empty = user profile

    // Font
    public double FontSize   { get; set; } = 14.0;
    public string FontFamily { get; set; } = "Cascadia Mono, Consolas, monospace";

    // Theme
    public string Theme { get; set; } = "Dark";

    // Environment variables applied to every terminal session.
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    // UI state
    public bool EnvironmentPanelIsExpanded { get; set; } = true;
    public int OpenTerminalCount { get; set; } = 1;
    public Dictionary<string, TilingLayoutSettings> TerminalTileLayouts { get; set; } = new();

    // Window & session persistence
    public WindowStateSettings?          Window                { get; set; }
    public double                        CurrentFontSize       { get; set; }   // 0 = not set, use FontSize
    public int                           SelectedTerminalIndex { get; set; }
    public List<TerminalSessionSnapshot> TerminalSnapshots     { get; set; } = new();

    // Logging
    public bool   EnableLogging        { get; set; } = true;
    public string LogDirectory         { get; set; } = DefaultLogDirectory();
    public long   MaxLogFileSizeBytes  { get; set; } = 50 * 1024 * 1024; // 50 MB
    public int    MaxLogFiles          { get; set; } = 100;
    public int    LogRetentionDays     { get; set; } = 30;
    public bool   MaskSensitiveInput   { get; set; } = true;

    private static string DefaultLogDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VibeTerm", "logs");
}

public sealed class TilingLayoutSettings
{
    public List<double> ColumnWeights { get; set; } = new();
    public List<double> RowWeights    { get; set; } = new();
}

public sealed class WindowStateSettings
{
    public double? Left        { get; set; }
    public double? Top         { get; set; }
    public double  Width       { get; set; } = 1200;
    public double  Height      { get; set; } = 750;
    public bool    IsMaximized { get; set; }
}

public sealed class TerminalSessionSnapshot
{
    public string DisplayName { get; set; } = string.Empty;
}
