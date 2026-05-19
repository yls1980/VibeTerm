using System;
using System.Runtime.InteropServices;

namespace VibeTerm.ConPTY;

/// <summary>
/// Managed wrapper for an HPCON (Windows Pseudo Console handle).
/// </summary>
internal sealed class PseudoConsole : IDisposable
{
    private IntPtr _handle;
    private bool   _disposed;

    internal IntPtr Handle => _handle;

    private PseudoConsole(IntPtr handle) => _handle = handle;

    internal static PseudoConsole Create(IntPtr inputReadSide, IntPtr outputWriteSide, short cols, short rows)
    {
        var size = new NativeMethods.COORD(cols, rows);
        int hr = NativeMethods.CreatePseudoConsole(size, inputReadSide, outputWriteSide, 0, out IntPtr hPC);
        if (hr != 0)
            Marshal.ThrowExceptionForHR(hr);
        return new PseudoConsole(hPC);
    }

    internal void Resize(short cols, short rows)
    {
        if (_disposed || _handle == IntPtr.Zero) return;
        NativeMethods.ResizePseudoConsole(_handle, new NativeMethods.COORD(cols, rows));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
