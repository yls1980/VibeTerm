namespace VibeTerm.Api;

/// <summary>
/// A point-in-time description of one terminal panel.
/// </summary>
public sealed record PanelSnapshot(
    int Id,
    int Index,
    string Name,
    string Status,
    bool Ready,
    string ShellPath,
    string? WorkingDirectory,
    bool Selected,
    uint? ExitCode,
    long OutputOffset);
