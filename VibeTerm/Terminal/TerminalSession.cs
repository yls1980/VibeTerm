using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VibeTerm.ConPTY;
using VibeTerm.Logging;

namespace VibeTerm.Terminal;

/// <summary>
/// Represents a single terminal session: a ConPTY process with its own
/// input/output streams, status, and output buffer.
/// </summary>
public sealed class TerminalSession : IDisposable
{
    private ConPtyProcess?            _process;
    private CancellationTokenSource   _cts = new();
    private Task?                     _pumpTask;
    private bool                      _disposed;

    private readonly object     _bufferLock            = new();
    private readonly List<byte[]> _outputBuffer        = new();
    private long                _outputBufferSizeBytes;
    private const long          MaxOutputBufferBytes   = 1 * 1024 * 1024; // 1 MB rolling

    public int             Id               { get; }
    public string          DisplayName      { get; set; }
    public string          ShellPath        { get; }
    public string?         WorkingDirectory { get; }
    public IReadOnlyDictionary<string, string> EnvironmentOverrides { get; private set; }
    public TerminalStatus  Status           { get; private set; } = TerminalStatus.Starting;
    public DateTime        CreatedAt        { get; } = DateTime.Now;
    public uint            ExitCode         { get; private set; }

    /// <summary>Raw byte data received from the process (VT sequences included).</summary>
    public event EventHandler<byte[]>? OutputReceived;
    public event EventHandler?         StatusChanged;
    public event EventHandler?         Exited;

    internal TerminalLoggingService? Logger { get; set; }

    public TerminalSession(
        int id,
        string displayName,
        string shellPath,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environmentOverrides = null)
    {
        Id               = id;
        DisplayName      = displayName;
        ShellPath        = shellPath;
        WorkingDirectory = workingDirectory;
        EnvironmentOverrides = new Dictionary<string, string>(
            environmentOverrides ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    public void Start(short cols = 120, short rows = 30)
    {
        try
        {
            string commandLine = BuildShellCommandLine();
            var environment = BuildEffectiveEnvironmentOverrides();

            _process = ConPtyProcess.Start(commandLine, WorkingDirectory, cols, rows, environment);
            Status = TerminalStatus.Active;
            RaiseStatusChanged();

            Logger?.Log(Id, LogEventType.SessionCreated,
                $"id={Id} name=\"{DisplayName}\" shell=\"{ShellPath}\" cwd=\"{WorkingDirectory ?? Environment.CurrentDirectory}\"");

            _pumpTask = Task.Run(() => PumpOutputAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            Status = TerminalStatus.Error;
            RaiseStatusChanged();
            Logger?.Log(Id, LogEventType.Error, $"Failed to start: {ex.Message}");
            throw;
        }
    }

    private string BuildShellCommandLine()
    {
        string quotedShell = QuoteCommandLineArgument(ShellPath);
        string fileName = Path.GetFileName(ShellPath);

        if (fileName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            return $"{quotedShell} -NoLogo -NoExit -Command \"if ($env:VIBETERM_PROFILE) {{ . $env:VIBETERM_PROFILE }}\"";
        }

        return quotedShell;
    }

    private IReadOnlyDictionary<string, string> BuildEffectiveEnvironmentOverrides()
    {
        var environment = new Dictionary<string, string>(EnvironmentOverrides, StringComparer.OrdinalIgnoreCase);
        string fileName = Path.GetFileName(ShellPath);

        if ((fileName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) ||
             fileName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase)) &&
            !environment.ContainsKey("POWERSHELL_UPDATECHECK"))
        {
            environment["POWERSHELL_UPDATECHECK"] = "Off";
        }

        return environment;
    }

    private static string QuoteCommandLineArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"") + "\"";

    private async Task PumpOutputAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];
        var stream = _process!.OutputStream;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Blocking read on a thread-pool thread
                int count = await Task.Run(() =>
                {
                    try { return stream.Read(buffer, 0, buffer.Length); }
                    catch { return 0; }
                }, ct).ConfigureAwait(false);

                if (count == 0) break; // EOF — process exited

                var data = new byte[count];
                Array.Copy(buffer, data, count);

                OutputReceived?.Invoke(this, data);
                AppendToBuffer(data);
                Logger?.LogOutput(Id, data);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger?.Log(Id, LogEventType.Error, ex.Message);
        }
        finally
        {
            await Task.Run(() => OnProcessExited()).ConfigureAwait(false);
        }
    }

    private void OnProcessExited()
    {
        if (_process != null && _process.HasExited(out uint code))
            ExitCode = code;

        if (Status != TerminalStatus.Error)
            Status = TerminalStatus.Exited;

        RaiseStatusChanged();
        Logger?.Log(Id, LogEventType.ProcessExited, $"exitCode={ExitCode}");
        Exited?.Invoke(this, EventArgs.Empty);
    }

    public async Task SendInputAsync(byte[] data)
    {
        if (_process == null || Status == TerminalStatus.Exited || Status == TerminalStatus.Error)
            return;

        try
        {
            string inputText = Encoding.UTF8.GetString(data);
            Logger?.LogInput(Id, inputText);

            await _process.InputStream.WriteAsync(data).ConfigureAwait(false);
            await _process.InputStream.FlushAsync().ConfigureAwait(false);
        }
        catch (IOException) { /* process likely exited */ }
        catch (ObjectDisposedException) { }
    }

    public async Task ApplyEnvironmentAsync(IReadOnlyDictionary<string, string> overrides)
    {
        var previous = EnvironmentOverrides;
        EnvironmentOverrides = new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);

        if (_process == null || Status == TerminalStatus.Exited || Status == TerminalStatus.Error)
            return;

        var removed = previous.Keys
            .Where(k => !EnvironmentOverrides.ContainsKey(k))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(k => string.Join("; ",
                $"Remove-Item -LiteralPath 'Env:\\{EscapePowerShellSingleQuoted(k)}' -ErrorAction SilentlyContinue",
                $"Remove-Variable -Name '{EscapePowerShellSingleQuoted(k)}' -Scope Global -ErrorAction SilentlyContinue"));

        var addedOrUpdated = EnvironmentOverrides
            .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => string.Join("; ",
                $"Set-Item -LiteralPath 'Env:\\{EscapePowerShellSingleQuoted(p.Key)}' -Value '{EscapePowerShellSingleQuoted(p.Value)}'",
                $"Set-Variable -Name '{EscapePowerShellSingleQuoted(p.Key)}' -Value '{EscapePowerShellSingleQuoted(p.Value)}' -Scope Global"));

        string script = string.Join(Environment.NewLine, removed.Concat(addedOrUpdated));

        if (string.IsNullOrWhiteSpace(script))
            return;

        await SendInputAsync(Encoding.UTF8.GetBytes(script + Environment.NewLine)).ConfigureAwait(false);
    }

    private static string EscapePowerShellSingleQuoted(string value) =>
        value.Replace("'", "''");

    private void AppendToBuffer(byte[] data)
    {
        lock (_bufferLock)
        {
            _outputBuffer.Add(data);
            _outputBufferSizeBytes += data.Length;
            while (_outputBufferSizeBytes > MaxOutputBufferBytes && _outputBuffer.Count > 0)
            {
                _outputBufferSizeBytes -= _outputBuffer[0].Length;
                _outputBuffer.RemoveAt(0);
            }
        }
    }

    public byte[] GetOutputSnapshot()
    {
        lock (_bufferLock)
        {
            using var ms = new MemoryStream((int)Math.Min(_outputBufferSizeBytes, int.MaxValue));
            foreach (var chunk in _outputBuffer)
                ms.Write(chunk, 0, chunk.Length);
            return ms.ToArray();
        }
    }

    public void Resize(short cols, short rows)
    {
        _process?.Resize(cols, rows);
        Logger?.Log(Id, LogEventType.Resize, $"cols={cols} rows={rows}");
    }

    public async Task StopAsync()
    {
        if (_disposed) return;

        Logger?.Log(Id, LogEventType.Close, "User closed terminal");

        _cts.Cancel();
        var process = _process;
        _process = null;
        process?.Kill();
        process?.Dispose();

        if (_pumpTask != null)
        {
            try { await Task.WhenAny(_pumpTask, Task.Delay(1000)).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch { }
        }
    }

    public void Rename(string newName)
    {
        string old = DisplayName;
        DisplayName = newName;
        Logger?.Log(Id, LogEventType.Rename, $"oldName=\"{old}\" newName=\"{newName}\"");
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _process?.Dispose();
        _cts.Dispose();
    }
}
