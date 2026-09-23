using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace ModernWakeup.Interop;

internal static class NativeMethods
{
    internal const uint GenericRead = 0x80000000;
    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint OpenExisting = 3;
    internal const uint FileAttributeNormal = 0x00000080;
    internal const int ErrorOperationAborted = 995;
    internal const int ErrorNoSuchDeviceInstance = unchecked((int)0xE000020B);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControlVersion(
        SafeFileHandle device,
        uint controlCode,
        nint inputBuffer,
        int inputBufferSize,
        out NativeVersion outputBuffer,
        int outputBufferSize,
        out uint bytesReturned,
        nint overlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControlEvent(
        SafeFileHandle device,
        uint controlCode,
        nint inputBuffer,
        int inputBufferSize,
        out NativeEvent outputBuffer,
        int outputBufferSize,
        out uint bytesReturned,
        nint overlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControlWait(
        SafeFileHandle device,
        uint controlCode,
        ref NativeWaitInput inputBuffer,
        int inputBufferSize,
        out NativeEvent outputBuffer,
        int outputBufferSize,
        out uint bytesReturned,
        nint overlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControlTimerInfo(
        SafeFileHandle device,
        uint controlCode,
        nint inputBuffer,
        int inputBufferSize,
        out NativeTimerInfo outputBuffer,
        int outputBufferSize,
        out uint bytesReturned,
        nint overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CancelIoEx(SafeFileHandle file, nint overlapped);

    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeWaitHandle CreateWaitableTimerEx(
        nint timerAttributes,
        string? timerName,
        uint flags,
        uint desiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWaitableTimerEx(
        SafeWaitHandle timer,
        ref long dueTime,
        int period,
        nint completionRoutine,
        nint completionArgument,
        ref ReasonContext wakeContext,
        uint tolerableDelay);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CancelWaitableTimer(SafeWaitHandle timer);

    [DllImport("powrprof.dll")]
    internal static extern uint CallNtPowerInformation(
        int informationLevel,
        nint inputBuffer,
        uint inputBufferLength,
        nint outputBuffer,
        uint outputBufferLength);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern nint SetupDiCreateDeviceInfoList(ref Guid classGuid, nint window);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiOpenDeviceInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiOpenDeviceInfo(
        nint deviceInfoSet,
        string instanceId,
        nint window,
        int flags,
        ref SpDevinfoData deviceInfo);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiCreateDeviceInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiCreateDeviceInfo(
        nint deviceInfoSet,
        string deviceName,
        ref Guid classGuid,
        string description,
        nint window,
        int flags,
        ref SpDevinfoData deviceInfo);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiSetDeviceRegistryPropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiSetDeviceRegistryProperty(
        nint deviceInfoSet,
        ref SpDevinfoData deviceInfo,
        int property,
        byte[] buffer,
        int bufferSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiCallClassInstaller(
        int installFunction,
        nint deviceInfoSet,
        ref SpDevinfoData deviceInfo);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeVersion
{
    internal uint Size;
    internal uint AbiVersion;
    internal uint DriverMajor;
    internal uint DriverMinor;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEvent
{
    internal uint Size;
    internal uint AbiVersion;
    internal ulong Sequence;
    internal long Timestamp100nsUtc;
    internal uint Source;
    internal uint Value;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeWaitInput
{
    internal uint Size;
    internal uint Reserved;
    internal ulong AfterSequence;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NativeTimerInfo
{
    internal uint Size;
    internal uint AbiVersion;
    internal long DueTime100nsUtc;
    internal uint Armed;
    internal uint PeriodMilliseconds;
    internal uint CallerType;
    internal uint ProcessId;
    internal uint ServiceTag;
    internal uint Reserved;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
    internal string ProcessImageName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
    internal string DeviceDescription;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
    internal string DevicePath;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
    internal string Reason;
}

[StructLayout(LayoutKind.Explicit, Size = 32)]
internal struct ReasonContext
{
    [FieldOffset(0)] internal uint Version;
    [FieldOffset(4)] internal uint Flags;
    [FieldOffset(8)] internal nint SimpleReasonString;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SpDevinfoData
{
    internal int Size;
    internal Guid ClassGuid;
    internal int DevInst;
    internal nint Reserved;

    internal static SpDevinfoData Create() => new() { Size = Marshal.SizeOf<SpDevinfoData>() };
}
