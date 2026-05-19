using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using VibeTerm.Settings;

namespace VibeTerm.Logging;

public sealed class TerminalLoggingService : IDisposable
{
    private readonly AppSettings                      _settings;
    private readonly SensitiveMasker                  _masker;
    private readonly Channel<LogEntry>                _channel;
    private readonly CancellationTokenSource          _cts = new();
    private readonly Dictionary<int, SessionLogState> _states = new();
    private readonly Task                             _worker;
    private bool                                      _disposed;

    public event EventHandler<string>? Warning;

    public TerminalLoggingService(AppSettings settings)
    {
        _settings = settings;
        _masker   = new SensitiveMasker(settings.MaskSensitiveInput);
        _channel  = Channel.CreateUnbounded<LogEntry>(
            new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
        _worker = Task.Run(ProcessEntriesAsync);
    }

    public void Log(int sessionId, LogEventType type, string message)
    {
        if (!_settings.EnableLogging) return;
        Enqueue(new LogEntry(sessionId, type, message));
    }

    public void LogInput(int sessionId, string text)
    {
        if (!_settings.EnableLogging) return;
        string masked = _masker.Mask(text);
        Enqueue(new LogEntry(sessionId, LogEventType.UserInput, masked));
    }

    public void LogOutput(int sessionId, byte[] rawBytes)
    {
        if (!_settings.EnableLogging) return;
        string text   = Encoding.UTF8.GetString(rawBytes);
        string masked = _masker.Mask(text);
        Enqueue(new LogEntry(sessionId, LogEventType.ProcessOutput, masked));
    }

    private void Enqueue(LogEntry entry) => _channel.Writer.TryWrite(entry);

    private async Task ProcessEntriesAsync()
    {
        await foreach (var entry in _channel.Reader.ReadAllAsync(_cts.Token))
        {
            try
            {
                var state = GetOrCreateState(entry.SessionId);
                if (state == null) continue;
                await WriteEntryAsync(state, entry).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Warning?.Invoke(this, $"Log write failed: {ex.Message}");
            }
        }
    }

    private SessionLogState? GetOrCreateState(int sessionId)
    {
        if (_states.TryGetValue(sessionId, out var state))
            return state;

        try
        {
            string dir = _settings.LogDirectory;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            CleanOldLogs(dir);

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string path      = Path.Combine(dir, $"terminal_{sessionId}_{timestamp}.log");

            state = new SessionLogState(sessionId, path, _settings.MaxLogFileSizeBytes);
            _states[sessionId] = state;
            return state;
        }
        catch (Exception ex)
        {
            Warning?.Invoke(this, $"Cannot create log for session {sessionId}: {ex.Message}");
            return null;
        }
    }

    private async Task WriteEntryAsync(SessionLogState state, LogEntry entry)
    {
        if (state.CurrentFileSizeBytes >= _settings.MaxLogFileSizeBytes)
            state.RollFile(_settings.LogDirectory);

        string line = FormatEntry(entry);
        byte[] bytes = Encoding.UTF8.GetBytes(line);

        await state.Writer.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        await state.Writer.FlushAsync().ConfigureAwait(false);
        state.CurrentFileSizeBytes += bytes.Length;
    }

    private static string FormatEntry(LogEntry e)
    {
        string tag  = e.Type.ToString().ToUpperInvariant().Replace("USERINFO", "USER_INPUT");
        string ts   = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
        return $"[{ts}] [{tag}] {e.Message}{Environment.NewLine}";
    }

    private void CleanOldLogs(string dir)
    {
        try
        {
            if (_settings.LogRetentionDays <= 0) return;
            var cutoff = DateTime.Now.AddDays(-_settings.LogRetentionDays);
            foreach (var file in Directory.GetFiles(dir, "terminal_*.log"))
            {
                if (File.GetCreationTime(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch { /* best-effort */ }
    }

    public void CloseSession(int sessionId)
    {
        if (!_states.TryGetValue(sessionId, out var state)) return;
        _channel.Writer.TryWrite(new LogEntry(sessionId, LogEventType.Close, "session closed"));
        // actual flush happens in the worker; dispose when all entries processed
        _ = Task.Run(async () =>
        {
            await Task.Delay(500).ConfigureAwait(false); // let queue drain
            state.Dispose();
            _states.Remove(sessionId);
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Writer.TryComplete();
        _cts.CancelAfter(TimeSpan.FromSeconds(3));
        try { _worker.Wait(TimeSpan.FromSeconds(4)); } catch { }
        foreach (var s in _states.Values) s.Dispose();
        _cts.Dispose();
    }

    // ─── inner types ──────────────────────────────────────────────────────────

    private sealed class LogEntry
    {
        internal int          SessionId { get; }
        internal LogEventType Type      { get; }
        internal string       Message   { get; }
        internal DateTime     Timestamp { get; } = DateTime.Now;
        internal LogEntry(int id, LogEventType t, string msg) { SessionId = id; Type = t; Message = msg; }
    }

    private sealed class SessionLogState : IDisposable
    {
        internal int          SessionId              { get; }
        internal string       CurrentPath            { get; private set; }
        internal FileStream   Writer                 { get; private set; }
        internal long         CurrentFileSizeBytes   { get; set; }
        private  long         _maxBytes;
        private  int          _partIndex = 1;

        internal SessionLogState(int id, string path, long maxBytes)
        {
            SessionId = id; CurrentPath = path; _maxBytes = maxBytes;
            Writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        }

        internal void RollFile(string dir)
        {
            Writer.Dispose();
            _partIndex++;
            string baseName = Path.GetFileNameWithoutExtension(CurrentPath);
            // Remove previous _partN suffix if present
            int partIdx = baseName.LastIndexOf("_part", StringComparison.Ordinal);
            if (partIdx > 0) baseName = baseName[..partIdx];
            string newPath = Path.Combine(dir, $"{baseName}_part{_partIndex}.log");
            CurrentPath = newPath;
            Writer = new FileStream(newPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            CurrentFileSizeBytes = 0;
        }

        public void Dispose()
        {
            try { Writer.Flush(); } catch { }
            Writer.Dispose();
        }
    }
}
