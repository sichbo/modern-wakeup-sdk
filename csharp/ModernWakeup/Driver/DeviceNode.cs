using ModernWakeup.Interop;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ModernWakeup.Driver;

internal static class DeviceNode
{
    internal const string InstanceId = @"ROOT\ModernWakeup\0000";
    private const string HardwareId = @"ROOT\ModernWakeup";
    private const int SpdrpHardwareId = 0x00000001;
    private const int DifRegisterDevice = 0x00000019;
    private static readonly Guid SystemClass = new("4d36e97d-e325-11ce-bfc1-08002be10318");

    internal static bool Exists()
    {
        nint set = OpenSet();
        try
        {
            SpDevinfoData info = SpDevinfoData.Create();
            if (NativeMethods.SetupDiOpenDeviceInfo(set, InstanceId, 0, 0, ref info))
                return true;
            int error = Marshal.GetLastWin32Error();
            if (error == NativeMethods.ErrorNoSuchDeviceInstance)
                return false;
            throw new Win32Exception(error, "Could not query the Modern Wakeup device node.");
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(set);
        }
    }

    internal static bool EnsureCreated()
    {
        nint set = OpenSet();
        try
        {
            SpDevinfoData info = SpDevinfoData.Create();
            if (NativeMethods.SetupDiOpenDeviceInfo(set, InstanceId, 0, 0, ref info))
                return false;
            int error = Marshal.GetLastWin32Error();
            if (error != NativeMethods.ErrorNoSuchDeviceInstance)
                throw new Win32Exception(error, "Could not query the Modern Wakeup device node.");

            Guid classGuid = SystemClass;
            if (!NativeMethods.SetupDiCreateDeviceInfo(
                    set, InstanceId, ref classGuid, "Modern Wakeup", 0, 0, ref info))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Could not create the Modern Wakeup device node.");

            byte[] ids = Encoding.Unicode.GetBytes(HardwareId + "\0\0");
            if (!NativeMethods.SetupDiSetDeviceRegistryProperty(
                    set, ref info, SpdrpHardwareId, ids, ids.Length))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Could not set the Modern Wakeup device hardware ID.");
            if (!NativeMethods.SetupDiCallClassInstaller(DifRegisterDevice, set, ref info))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Could not register the Modern Wakeup device node.");
            return true;
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static nint OpenSet()
    {
        Guid classGuid = SystemClass;
        nint set = NativeMethods.SetupDiCreateDeviceInfoList(ref classGuid, 0);
        if (set == new nint(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not open the Windows System device class.");
        return set;
    }
}

