using System.IO;
using System.Reflection;
using VibeTerm.Logging;
using VibeTerm.Settings;
using VibeTerm.Terminal;
using Xunit;

namespace VibeTerm.Tests;

public sealed class TerminalSessionManagerTests
{
    [Fact]
    public void CreateSessionUsesConfiguredShellAndWorkingDirectory()
    {
        using var temp = new TempDirectory();
        string shellPath = Path.Combine(temp.Path, "pwsh.exe");
        File.WriteAllText(shellPath, string.Empty);
        string startupDirectory = Path.Combine(temp.Path, "workspace");
        Directory.CreateDirectory(startupDirectory);

        using var logger = CreateDisabledLogger(temp.Path);
        using var manager = new TerminalSessionManager(logger);
        var settings = new AppSettings
        {
            ShellPath = shellPath,
            StartupDirectory = startupDirectory,
            EnableLogging = false,
            LogDirectory = temp.Path
        };

        TerminalSession? added = null;
        manager.SessionAdded += (_, session) => added = session;

        var created = manager.CreateSession(settings);

        Assert.Same(created, added);
        Assert.Equal(1, created.Id);
        Assert.Equal("PowerShell 1", created.DisplayName);
        Assert.Equal(shellPath, created.ShellPath);
        Assert.Equal(startupDirectory, created.WorkingDirectory);
        Assert.Single(manager.Sessions);
    }

    [Theory]
    [InlineData(TerminalStatus.Starting, false)]
    [InlineData(TerminalStatus.Active, true)]
    [InlineData(TerminalStatus.Running, true)]
    [InlineData(TerminalStatus.Exited, false)]
    [InlineData(TerminalStatus.Error, false)]
    public void HasActiveSessionsReflectsRunningModes(TerminalStatus status, bool expected)
    {
        using var temp = new TempDirectory();
        using var logger = CreateDisabledLogger(temp.Path);
        using var manager = new TerminalSessionManager(logger);
        var session = manager.CreateSession(new AppSettings { EnableLogging = false, LogDirectory = temp.Path });
        SetStatus(session, status);

        Assert.Equal(expected, manager.HasActiveSessions());
    }

    [Fact]
    public async Task CloseSessionRemovesSessionAndRaisesEvent()
    {
        using var temp = new TempDirectory();
        using var logger = CreateDisabledLogger(temp.Path);
        using var manager = new TerminalSessionManager(logger);
        var session = manager.CreateSession(new AppSettings { EnableLogging = false, LogDirectory = temp.Path });

        TerminalSession? removed = null;
        manager.SessionRemoved += (_, removedSession) => removed = removedSession;

        await manager.CloseSessionAsync(session);

        Assert.Same(session, removed);
        Assert.Empty(manager.Sessions);
    }

    private static TerminalLoggingService CreateDisabledLogger(string logDirectory) =>
        new(new AppSettings
        {
            EnableLogging = false,
            LogDirectory = logDirectory
        });

    private static void SetStatus(TerminalSession session, TerminalStatus status)
    {
        typeof(TerminalSession)
            .GetProperty(nameof(TerminalSession.Status), BindingFlags.Instance | BindingFlags.Public)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(session, new object[] { status });
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "VibeTerm.Tests",
            Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
