using Microsoft.Win32.SafeHandles;
using ModernWakeup.Interop;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ModernWakeup;

/// <summary>
/// Owns an ordinary Windows resumable waitable timer. Keep this object alive until the timer fires.
/// </summary>
public sealed class WakeTimer : IDisposable
{
    private const uint CreateWaitableTimerManualReset = 0x00000001;
    private const uint TimerModifyStateAndSynchronize = 0x00100002;
    private const uint DiagnosticReasonSimpleString = 0x00000001;

    private readonly SafeWaitHandle _handle;
    private readonly NativeWaitHandle _waitHandle;
    private bool _disposed;

    internal WakeTimer(DateTimeOffset dueTime, string reason)
    {
        if (dueTime <= DateTimeOffset.UtcNow)
            throw new ArgumentOutOfRangeException(nameof(dueTime), "The wake time must be in the future.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Supply a short diagnostic reason for the wake timer.", nameof(reason));

        _handle = NativeMethods.CreateWaitableTimerEx(
            0, null, CreateWaitableTimerManualReset, TimerModifyStateAndSynchronize);
        if (_handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new Win32Exception(error, "CreateWaitableTimerEx failed.");
        }

        _waitHandle = new NativeWaitHandle(_handle);
        nint reasonPointer = Marshal.StringToHGlobalUni(reason);
        try
        {
            ReasonContext context = new()
            {
                Version = 0,
                Flags = DiagnosticReasonSimpleString,
                SimpleReasonString = reasonPointer,
            };
            long due = dueTime.UtcDateTime.ToFileTimeUtc();
            if (!NativeMethods.SetWaitableTimerEx(
                    _handle, ref due, 0, 0, 0, ref context, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWaitableTimerEx failed.");
        }
        catch
        {
            _waitHandle.Dispose();
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(reasonPointer);
        }

        DueTime = dueTime;
        Reason = reason;
    }

    public DateTimeOffset DueTime { get; }

    public string Reason { get; }

    /// <summary>Waits until the underlying Windows timer becomes signaled.</summary>
    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.Run(() =>
        {
            int index = WaitHandle.WaitAny([_waitHandle, cancellationToken.WaitHandle]);
            if (index == 1)
                cancellationToken.ThrowIfCancellationRequested();
        }, CancellationToken.None);
    }

    /// <summary>Cancels the timer without disposing this object.</summary>
    public void Cancel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!NativeMethods.CancelWaitableTimer(_handle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CancelWaitableTimer failed.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        NativeMethods.CancelWaitableTimer(_handle);
        _waitHandle.Dispose();
    }

    private sealed class NativeWaitHandle : WaitHandle
    {
        internal NativeWaitHandle(SafeWaitHandle handle)
        {
            SafeWaitHandle = handle;
        }
    }
}
