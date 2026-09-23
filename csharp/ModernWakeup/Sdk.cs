using ModernWakeup.Driver;

namespace ModernWakeup;

/// <summary>High-level Modern Wakeup installation, configuration, status, and timer helpers.</summary>
public static class Sdk
{
    /// <summary>Gets whether this PC advertises S0 low-power idle (Modern Standby).</summary>
    public static Task<bool> IsModernStandbyEnabled => Task.FromResult(Platform.IsModernStandbyEnabled());

    /// <summary>Installs the embedded Microsoft-signed x64 driver and configures its allowlist.</summary>
    /// <remarks>The caller must already be running as Administrator.</remarks>
    public static Task<Result> InstallDriver(IEnumerable<string> allowedApps) =>
        InstallDriver(new InstallOptions { AllowedApplications = allowedApps });

    /// <summary>Installs the embedded Microsoft-signed x64 driver with advanced options.</summary>
    /// <remarks>The caller must already be running as Administrator.</remarks>
    public static Task<Result> InstallDriver(InstallOptions options) =>
        Task.Run(() => DriverInstaller.Install(options));

    /// <summary>Alias following the standard .NET asynchronous naming convention.</summary>
    public static Task<Result> InstallDriverAsync(IEnumerable<string> allowedApps) => InstallDriver(allowedApps);

    /// <summary>Updates the allowlist and restarts the device so the driver reloads it.</summary>
    public static Task<Result> ConfigureAllowedApps(
        IEnumerable<string> allowedApps,
        string? licenseKey = null) =>
        Task.Run(() => DriverInstaller.Configure(allowedApps, licenseKey));

    /// <summary>Removes the root device, all matching driver-store packages, and active allowlist.</summary>
    /// <remarks>The caller must already be running as Administrator.</remarks>
    public static Task<Result> UninstallDriver() => Task.Run(DriverInstaller.Uninstall);

    /// <summary>Alias following the standard .NET asynchronous naming convention.</summary>
    public static Task<Result> UninstallDriverAsync() => UninstallDriver();

    /// <summary>Returns current platform, install, ABI, allowlist, and trial status.</summary>
    public static Task<ModernWakeupStatus> GetStatusAsync() => Task.Run(() =>
    {
        bool modernStandby = Platform.IsModernStandbyEnabled();
        bool installed = DriverInstaller.IsInstalled();
        bool available = ModernWakeupClient.TryOpen(out ModernWakeupClient? client);
        ModernWakeupVersion? version = null;
        if (client is not null)
        {
            using (client)
                version = client.GetVersion();
        }

        return new ModernWakeupStatus(
            modernStandby,
            installed,
            available,
            DriverInstaller.AllowlistPath,
            File.Exists(DriverInstaller.AllowlistPath),
            version,
            DriverInstaller.ReadTrial());
    });

    /// <summary>
    /// Creates a normal Windows resumable timer. Modern Wakeup discovers it through Windows; no driver IOCTL arms it.
    /// </summary>
    public static WakeTimer CreateWakeTimer(DateTimeOffset dueTime, string reason) => new(dueTime, reason);
}
