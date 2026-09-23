using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using ModernWakeup.Interop;

namespace ModernWakeup;

internal static class Platform
{
    // SYSTEM_POWER_CAPABILITIES is 76 bytes. AoAc is the BOOLEAN at byte 20.
    private const int SystemPowerCapabilities = 4;
    private const int CapabilitiesSize = 76;
    private const int AoAcOffset = 20;

    internal static bool IsModernStandbyEnabled()
    {
        EnsureWindows();
        nint buffer = Marshal.AllocHGlobal(CapabilitiesSize);
        try
        {
            Span<byte> zero = stackalloc byte[CapabilitiesSize];
            Marshal.Copy(zero.ToArray(), 0, buffer, CapabilitiesSize);
            uint status = NativeMethods.CallNtPowerInformation(
                SystemPowerCapabilities, 0, 0, buffer, CapabilitiesSize);
            if (status != 0)
                throw new Win32Exception(unchecked((int)status),
                    $"Could not query Windows power capabilities (NTSTATUS 0x{status:X8}).");
            return Marshal.ReadByte(buffer, AoAcOffset) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static bool IsAdministrator()
    {
        EnsureWindows();
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static void EnsureSupportedInstallerPlatform()
    {
        EnsureWindows();
        if (!Environment.Is64BitOperatingSystem)
            throw new PlatformNotSupportedException("The signed Modern Wakeup driver package supports x64 Windows only.");
        if (!IsAdministrator())
            throw new UnauthorizedAccessException(
                "Driver installation and removal require an elevated Administrator process.");
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Modern Wakeup is available only on Windows.");
    }
}
