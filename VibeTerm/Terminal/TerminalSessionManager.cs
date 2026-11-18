using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VibeTerm.Logging;
using VibeTerm.Settings;

namespace VibeTerm.Terminal;

/// <summary>
/// Creates, tracks, and disposes all terminal sessions.
/// </summary>
public sealed class TerminalSessionManager : IDisposable
{
    private readonly List<TerminalSession> _sessions = new();
    private readonly TerminalLoggingService _logger;
    private int _nextId = 1;

    public IReadOnlyList<TerminalSession> Sessions => _sessions;

    public event EventHandler<TerminalSession>? SessionAdded;
    public event EventHandler<TerminalSession>? SessionRemoved;

    public TerminalSessionManager(TerminalLoggingService logger)
    {
        _logger = logger;
    }

    public TerminalSession CreateSession(
        AppSettings settings,
        string? displayName = null,
        string? workingDirectory = null)
    {
        string shell = ResolveShell(settings.ShellPath);
        string? cwd  = ResolveWorkingDirectory(workingDirectory, settings.StartupDirectory);

        int id      = _nextId++;
        string name = displayName ?? $"PowerShell {id}";

        var session = new TerminalSession(id, name, shell, cwd, settings.EnvironmentVariables)
        {
            Logger = _logger
        };

        _sessions.Add(session);
        SessionAdded?.Invoke(this, session);
        return session;
    }

    public async Task CloseSessionAsync(TerminalSession session)
    {
        await session.StopAsync().ConfigureAwait(false);
        _sessions.Remove(session);
        session.Dispose();
        SessionRemoved?.Invoke(this, session);
    }

    public bool HasActiveSessions() =>
        _sessions.Any(s => s.Status == TerminalStatus.Active || s.Status == TerminalStatus.Running);

    private static string ResolveShell(string configuredShell)
    {
        // Prefer configured shell if it exists
        if (!string.IsNullOrWhiteSpace(configuredShell) && File.Exists(configuredShell))
            return configuredShell;

        // Try pwsh.exe (PowerShell 7+) first
        string[] pwshCandidates = new[]
        {
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Program Files\PowerShell\7-preview\pwsh.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"PowerShell\7\pwsh.exe"),
        };

        foreach (var p in pwshCandidates)
            if (File.Exists(p)) return p;

        // Search PATH for pwsh
        string? pwshInPath = FindInPath("pwsh.exe");
        if (pwshInPath != null) return pwshInPath;

        // Fall back to Windows PowerShell
        string winPs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe");
        if (File.Exists(winPs)) return winPs;

        return "powershell.exe"; // last resort — rely on PATH
    }

    private static string? FindInPath(string exe)
    {
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (pathVar == null) return null;

        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* ignore bad path entries */ }
        }
        return null;
    }

    private static string? ResolveWorkingDirectory(string? restored, string? configured)
    {
        foreach (string? candidate in new[] { restored, configured })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public void Dispose()
    {
        foreach (var s in _sessions.ToList())
            s.Dispose();
        _sessions.Clear();
    }
}
