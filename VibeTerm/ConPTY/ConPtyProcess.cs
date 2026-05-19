using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace VibeTerm.ConPTY;

/// <summary>
/// Launches a child process attached to a Windows Pseudo Console (ConPTY).
/// Provides streams for reading the process output and writing input.
/// </summary>
internal sealed class ConPtyProcess : IDisposable
{
    private PseudoConsole?               _pty;
    private NativeMethods.PROCESS_INFORMATION _procInfo;
    private IntPtr _attrList  = IntPtr.Zero;

    // Pipe ends held by our side (app reads output, writes input)
    private IntPtr _outputReadSide  = IntPtr.Zero;
    private IntPtr _inputWriteSide  = IntPtr.Zero;

    // Pipe ends given to the pseudo console (closed after CreateProcess)
    private IntPtr _inputReadSide   = IntPtr.Zero;
    private IntPtr _outputWriteSide = IntPtr.Zero;

    private FileStream? _outputStream;
    private FileStream? _inputStream;
    private bool        _disposed;

    internal int    ProcessId    => _procInfo.dwProcessId;
    internal Stream OutputStream => _outputStream!;
    internal Stream InputStream  => _inputStream!;

    internal static ConPtyProcess Start(
        string command,
        string? workingDirectory,
        short cols,
        short rows,
        IReadOnlyDictionary<string, string>? environmentOverrides = null)
    {
        var p = new ConPtyProcess();
        p.Initialize(command, workingDirectory, cols, rows, environmentOverrides);
        return p;
    }

    private void Initialize(
        string command,
        string? workingDirectory,
        short cols,
        short rows,
        IReadOnlyDictionary<string, string>? environmentOverrides)
    {
        var sa = new NativeMethods.SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>()
        };

        if (!NativeMethods.CreatePipe(out _inputReadSide, out _inputWriteSide, ref sa, 0))
            throw new InvalidOperationException($"CreatePipe (input) failed: {Marshal.GetLastWin32Error()}");

        if (!NativeMethods.CreatePipe(out _outputReadSide, out _outputWriteSide, ref sa, 0))
            throw new InvalidOperationException($"CreatePipe (output) failed: {Marshal.GetLastWin32Error()}");

        _pty = PseudoConsole.Create(_inputReadSide, _outputWriteSide, cols, rows);

        var startupInfo = BuildStartupInfoEx(_pty.Handle);
        uint flags = NativeMethods.EXTENDED_STARTUPINFO_PRESENT | NativeMethods.CREATE_UNICODE_ENVIRONMENT;
        IntPtr environmentBlock = BuildEnvironmentBlock(environmentOverrides);

        bool ok;
        try
        {
            ok = NativeMethods.CreateProcess(
                null, command,
                IntPtr.Zero, IntPtr.Zero,
                false, flags,
                environmentBlock, workingDirectory,
                ref startupInfo, out _procInfo);
        }
        finally
        {
            if (environmentBlock != IntPtr.Zero)
                Marshal.FreeHGlobal(environmentBlock);
        }

        // Attribute list is only needed during CreateProcess
        if (_attrList != IntPtr.Zero)
        {
            NativeMethods.DeleteProcThreadAttributeList(_attrList);
            Marshal.FreeHGlobal(_attrList);
            _attrList = IntPtr.Zero;
        }

        if (!ok)
            throw new InvalidOperationException($"CreateProcess failed: {Marshal.GetLastWin32Error()}");

        // PTY-side handles are now owned by the pseudo console — release our copies
        NativeMethods.CloseHandle(_inputReadSide);   _inputReadSide   = IntPtr.Zero;
        NativeMethods.CloseHandle(_outputWriteSide); _outputWriteSide = IntPtr.Zero;

        // Wrap our pipe ends in streams (SafeFileHandle takes ownership of the handle)
        _outputStream = new FileStream(
            new SafeFileHandle(ExchangeHandle(ref _outputReadSide), ownsHandle: true),
            FileAccess.Read, bufferSize: 4096, isAsync: false);

        _inputStream = new FileStream(
            new SafeFileHandle(ExchangeHandle(ref _inputWriteSide), ownsHandle: true),
            FileAccess.Write, bufferSize: 4096, isAsync: false);
    }

    private static IntPtr BuildEnvironmentBlock(IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides == null || overrides.Count == 0)
            return IntPtr.Zero;

        var env = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .ToDictionary(
                e => (string)e.Key,
                e => (string?)e.Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        foreach (var pair in overrides)
        {
            string name = pair.Key.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Contains('=') || name.Contains('\0'))
                continue;

            env[name] = pair.Value ?? string.Empty;
        }

        string? profile = BuildPowerShellProfile(overrides);
        if (profile != null)
        {
            env["VIBETERM_PROFILE"] = profile;
            env["PSModulePath"] = env.TryGetValue("PSModulePath", out var psModulePath)
                ? psModulePath
                : string.Empty;
        }

        var block = new StringBuilder();
        foreach (var pair in env.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            block.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        block.Append('\0');

        return Marshal.StringToHGlobalUni(block.ToString());
    }

    private static string? BuildPowerShellProfile(IReadOnlyDictionary<string, string> overrides)
    {
        var assignments = overrides
            .Where(p => IsValidPowerShellVariableName(p.Key))
            .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => $"Set-Variable -Name '{EscapePowerShellSingleQuoted(p.Key.Trim())}' -Value '{EscapePowerShellSingleQuoted(p.Value ?? string.Empty)}' -Scope Global")
            .ToArray();

        if (assignments.Length == 0)
            return null;

        string profilePath = Path.Combine(
            Path.GetTempPath(),
            "VibeTerm",
            $"profile_{Guid.NewGuid():N}.ps1");

        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        File.WriteAllLines(profilePath, assignments, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return profilePath;
    }

    private static bool IsValidPowerShellVariableName(string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        foreach (char ch in name)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                return false;
        }

        return true;
    }

    private static string EscapePowerShellSingleQuoted(string value) =>
        value.Replace("'", "''");

    private static IntPtr ExchangeHandle(ref IntPtr h)
    {
        var v = h;
        h = IntPtr.Zero;
        return v;
    }

    private NativeMethods.STARTUPINFOEX BuildStartupInfoEx(IntPtr hPC)
    {
        IntPtr listSize = IntPtr.Zero;
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref listSize);

        _attrList = Marshal.AllocHGlobal(listSize);
        if (!NativeMethods.InitializeProcThreadAttributeList(_attrList, 1, 0, ref listSize))
            throw new InvalidOperationException($"InitializeProcThreadAttributeList failed: {Marshal.GetLastWin32Error()}");

        // PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE: lpValue = the HPCON value itself (sizeof HANDLE)
        if (!NativeMethods.UpdateProcThreadAttribute(
            _attrList, 0,
            NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
            hPC,
            new IntPtr(IntPtr.Size),
            IntPtr.Zero, IntPtr.Zero))
            throw new InvalidOperationException($"UpdateProcThreadAttribute failed: {Marshal.GetLastWin32Error()}");

        return new NativeMethods.STARTUPINFOEX
        {
            StartupInfo = new NativeMethods.STARTUPINFO
            {
                cb = Marshal.SizeOf<NativeMethods.STARTUPINFOEX>()
            },
            lpAttributeList = _attrList
        };
    }

    internal void Resize(short cols, short rows) => _pty?.Resize(cols, rows);

    internal bool HasExited(out uint exitCode)
    {
        if (_procInfo.hProcess == IntPtr.Zero) { exitCode = 0; return true; }
        NativeMethods.GetExitCodeProcess(_procInfo.hProcess, out exitCode);
        return exitCode != NativeMethods.STILL_ACTIVE;
    }

    internal void Kill()
    {
        if (_procInfo.hProcess != IntPtr.Zero)
            NativeMethods.TerminateProcess(_procInfo.hProcess, 1);
    }

    internal Task WaitForExitAsync(CancellationToken ct = default) =>
        Task.Run(() => NativeMethods.WaitForSingleObject(_procInfo.hProcess, NativeMethods.INFINITE), ct);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Dispose streams first (closes pipe handles)
        _outputStream?.Dispose();
        _inputStream?.Dispose();

        // Close pseudo console (this also signals EOF to the child process)
        _pty?.Dispose();

        // Close process handles
        if (_procInfo.hProcess != IntPtr.Zero) { NativeMethods.CloseHandle(_procInfo.hProcess); _procInfo.hProcess = IntPtr.Zero; }
        if (_procInfo.hThread  != IntPtr.Zero) { NativeMethods.CloseHandle(_procInfo.hThread);  _procInfo.hThread  = IntPtr.Zero; }

        // Clean up any un-transferred pipe handles (shouldn't be set after successful init)
        if (_inputReadSide   != IntPtr.Zero) { NativeMethods.CloseHandle(_inputReadSide);   }
        if (_outputWriteSide != IntPtr.Zero) { NativeMethods.CloseHandle(_outputWriteSide); }
        if (_outputReadSide  != IntPtr.Zero) { NativeMethods.CloseHandle(_outputReadSide);  }
        if (_inputWriteSide  != IntPtr.Zero) { NativeMethods.CloseHandle(_inputWriteSide);  }

        if (_attrList != IntPtr.Zero)
        {
            NativeMethods.DeleteProcThreadAttributeList(_attrList);
            Marshal.FreeHGlobal(_attrList);
        }
    }
}
