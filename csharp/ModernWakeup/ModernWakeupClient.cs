using Microsoft.Win32.SafeHandles;
using ModernWakeup.Interop;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ModernWakeup;

/// <summary>A read-only client for the Modern Wakeup driver's public diagnostics ABI.</summary>
public sealed class ModernWakeupClient : IDisposable
{
    public const string DevicePath = @"\\.\ModernWakeup";
    public const uint SupportedAbiVersion = 2;

    private const uint DeviceType = 0x8000;
    private const uint FileReadData = 0x0001;
    private static readonly uint IoctlGetVersion = CtlCode(DeviceType, 0x800, 0, FileReadData);
    private static readonly uint IoctlGetLastEvent = CtlCode(DeviceType, 0x801, 0, FileReadData);
    private static readonly uint IoctlWaitEvent = CtlCode(DeviceType, 0x802, 0, FileReadData);
    private static readonly uint IoctlGetWakeTimerInfo = CtlCode(DeviceType, 0x803, 0, FileReadData);

    private readonly SafeFileHandle _device;
    private bool _disposed;

    private ModernWakeupClient(SafeFileHandle device) => _device = device;

    /// <summary>Opens the driver's read-only control device.</summary>
    public static ModernWakeupClient Open() => new(OpenDevice());

    /// <summary>Returns and validates the installed driver's ABI version.</summary>
    public ModernWakeupVersion GetVersion()
    {
        ThrowIfDisposed();
        if (!NativeMethods.DeviceIoControlVersion(
                _device, IoctlGetVersion, 0, 0, out NativeVersion value,
                Marshal.SizeOf<NativeVersion>(), out uint returned, 0))
            ThrowLastWin32("GET_VERSION failed");

        ValidateSize<NativeVersion>(returned, value.Size, value.AbiVersion, "GET_VERSION");
        return new ModernWakeupVersion(value.AbiVersion, value.DriverMajor, value.DriverMinor);
    }

    /// <summary>Returns the most recently published driver event.</summary>
    public WakeupEvent GetLastEvent()
    {
        ThrowIfDisposed();
        if (!NativeMethods.DeviceIoControlEvent(
                _device, IoctlGetLastEvent, 0, 0, out NativeEvent value,
                Marshal.SizeOf<NativeEvent>(), out uint returned, 0))
            ThrowLastWin32("GET_LAST_EVENT failed");

        ValidateSize<NativeEvent>(returned, value.Size, value.AbiVersion, "GET_LAST_EVENT");
        return Convert(value);
    }

    /// <summary>Returns the earliest allowed timer currently mirrored by the driver.</summary>
    public WakeTimerInfo GetWakeTimerInfo()
    {
        ThrowIfDisposed();
        if (!NativeMethods.DeviceIoControlTimerInfo(
                _device, IoctlGetWakeTimerInfo, 0, 0, out NativeTimerInfo value,
                Marshal.SizeOf<NativeTimerInfo>(), out uint returned, 0))
            ThrowLastWin32("GET_WAKE_TIMER_INFO failed");

        ValidateSize<NativeTimerInfo>(returned, value.Size, value.AbiVersion, "GET_WAKE_TIMER_INFO");
        if (value.Reserved != 0)
            throw new ModernWakeupException("GET_WAKE_TIMER_INFO returned non-zero reserved data.");

        DateTimeOffset? due = value.Armed != 0 && value.DueTime100nsUtc > 0
            ? DateTimeOffset.FromFileTime(value.DueTime100nsUtc)
            : null;
        return new WakeTimerInfo(
            value.Armed != 0,
            due,
            value.PeriodMilliseconds,
            (WakeTimerCallerType)value.CallerType,
            value.ProcessId,
            value.ServiceTag,
            value.ProcessImageName ?? string.Empty,
            value.DeviceDescription ?? string.Empty,
            value.DevicePath ?? string.Empty,
            value.Reason ?? string.Empty);
    }

    /// <summary>
    /// Streams driver events after a sequence cursor. Cancelling the token cancels the pending driver request.
    /// </summary>
    public async IAsyncEnumerable<WakeupEvent> WatchEventsAsync(
        ulong afterSequence = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using SafeFileHandle monitor = OpenDevice();
        ulong sequence = afterSequence;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeEvent value;
            uint returned;
            bool success;
            int error;

            using CancellationTokenRegistration registration = cancellationToken.Register(
                static handle => NativeMethods.CancelIoEx((SafeFileHandle)handle!, 0), monitor);

            (success, value, returned, error) = await Task.Run(() =>
            {
                NativeWaitInput input = new()
                {
                    Size = (uint)Marshal.SizeOf<NativeWaitInput>(),
                    Reserved = 0,
                    AfterSequence = sequence,
                };
                bool ok = NativeMethods.DeviceIoControlWait(
                    monitor, IoctlWaitEvent, ref input, Marshal.SizeOf<NativeWaitInput>(),
                    out NativeEvent nativeEvent, Marshal.SizeOf<NativeEvent>(), out uint count, 0);
                return (ok, nativeEvent, count, ok ? 0 : Marshal.GetLastWin32Error());
            }, CancellationToken.None).ConfigureAwait(false);

            if (!success)
            {
                if (cancellationToken.IsCancellationRequested && error == NativeMethods.ErrorOperationAborted)
                    throw new OperationCanceledException(cancellationToken);
                throw CreateWin32Exception(error, "WAIT_EVENT failed");
            }

            ValidateSize<NativeEvent>(returned, value.Size, value.AbiVersion, "WAIT_EVENT");
            sequence = value.Sequence;
            yield return Convert(value);
        }
    }

    /// <summary>Returns a friendly description for a status event value.</summary>
    public static string DescribeStatusMessage(WakeupStatusMessage message) => message switch
    {
        WakeupStatusMessage.InvalidLicense => "The driver could not validate a license - a license can be obtained at modernwakeup.com; wake-timer mirroring is disabled.",
        WakeupStatusMessage.TimerSelected => "The driver selected and mirrored an allowed wake timer.",
        WakeupStatusMessage.TimerExecuted => "The driver executed its scheduled wakeup.",
        WakeupStatusMessage.TimerCleared => "The driver cleared its mirror because no allowed wake timer remains.",
        WakeupStatusMessage.ValidLicense => "The driver validated its license.",
        >= WakeupStatusMessage.TrialWakeOneDayRemaining and <= WakeupStatusMessage.TrialWakeSevenDaysRemaining =>
            $"The driver woke the system using its trial; {(uint)message - 5} day(s) remain.",
        WakeupStatusMessage.TrialWakeExpired => "The trial wake executed after its delayed deadline; future wakeups require a license.",
        _ => $"Unknown Modern Wakeup status message {(uint)message}.",
    };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        NativeMethods.CancelIoEx(_device, 0);
        _device.Dispose();
    }

    internal static bool TryOpen(out ModernWakeupClient? client)
    {
        try
        {
            client = Open();
            return true;
        }
        catch (Win32Exception)
        {
            client = null;
            return false;
        }
    }

    private static SafeFileHandle OpenDevice()
    {
        SafeFileHandle device = NativeMethods.CreateFile(
            DevicePath,
            NativeMethods.GenericRead,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            0,
            NativeMethods.OpenExisting,
            NativeMethods.FileAttributeNormal,
            0);
        if (!device.IsInvalid)
            return device;

        int error = Marshal.GetLastWin32Error();
        device.Dispose();
        throw CreateWin32Exception(error, $"Could not open {DevicePath}");
    }

    private static WakeupEvent Convert(NativeEvent value)
    {
        DateTimeOffset timestamp = value.Timestamp100nsUtc > 0
            ? DateTimeOffset.FromFileTime(value.Timestamp100nsUtc)
            : DateTimeOffset.MinValue;
        return new WakeupEvent(value.Sequence, timestamp, (WakeupEventSource)value.Source, value.Value);
    }

    private static void ValidateSize<T>(uint returned, uint reported, uint abi, string operation)
    {
        uint expected = (uint)Marshal.SizeOf<T>();
        if (returned != expected || reported != expected || abi != SupportedAbiVersion)
            throw new ModernWakeupException(
                $"{operation} returned an incompatible result (ABI {abi}, size {reported}, bytes {returned}); " +
                $"expected ABI {SupportedAbiVersion} and {expected} bytes.");
    }

    private static uint CtlCode(uint deviceType, uint function, uint method, uint access) =>
        (deviceType << 16) | (access << 14) | (function << 2) | method;

    private static void ThrowLastWin32(string operation) =>
        throw CreateWin32Exception(Marshal.GetLastWin32Error(), operation);

    private static Win32Exception CreateWin32Exception(int error, string operation) =>
        new(error, $"{operation} (Win32 error {error}: {new Win32Exception(error).Message})");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
