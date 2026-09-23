using Microsoft.Win32;
using System.Text;
using System.Xml.Linq;

namespace ModernWakeup.Driver;

internal static class DriverInstaller
{
    private const string ServiceRegistryPath = @"SYSTEM\CurrentControlSet\Services\ModernWakeup";
    private const string TrialStartedValueName = "TrialStartedUtc";
    private const int TrialDays = 7;

    internal static readonly string ConfigurationDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ModernWakeup");

    internal static readonly string AllowlistPath = Path.Combine(
        ConfigurationDirectory, "ModernWakeup-Allowed-Timers.txt");

    internal static Result Install(InstallOptions options)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(options);
            Platform.EnsureSupportedInstallerPlatform();

            string? license = options.LicenseKey ?? Allowlist.ReadExistingLicense(AllowlistPath);
            string allowlist = Allowlist.Build(options.AllowedApplications, license);
            Directory.CreateDirectory(ConfigurationDirectory);
            WriteAllowlistAtomically(allowlist);

            string staging = DriverPackage.Extract(ConfigurationDirectory);
            bool created = false;
            try
            {
                string inf = Path.Combine(staging, "ModernWakeup.Driver.inf");
                CommandResult stage = CommandRunner.Run("pnputil.exe", "/add-driver", inf);
                stage.EnsureSuccess("Staging the Modern Wakeup driver package", 259);

                created = DeviceNode.EnsureCreated();
                CommandResult install = CommandRunner.Run("pnputil.exe", "/add-driver", inf, "/install");
                install.EnsureSuccess("Installing the Modern Wakeup driver package", 259);

                using RegistryKey service = Registry.LocalMachine.CreateSubKey(ServiceRegistryPath, writable: true)
                    ?? throw new InvalidOperationException("Windows did not expose the ModernWakeup service registry key.");
                service.SetValue("AllowlistPath", @"\??\" + AllowlistPath, RegistryValueKind.String);
                object? trialStart = service.GetValue(TrialStartedValueName);
                if (trialStart is not long start || start <= 0 || start > DateTimeOffset.UtcNow.ToFileTime())
                    service.SetValue(TrialStartedValueName, DateTimeOffset.UtcNow.ToFileTime(), RegistryValueKind.QWord);

                CommandResult restart = CommandRunner.Run(
                    "pnputil.exe", "/restart-device", DeviceNode.InstanceId);
                restart.EnsureSuccess("Restarting the Modern Wakeup device");

                using ModernWakeupClient client = ModernWakeupClient.Open();
                ModernWakeupVersion version = client.GetVersion();
                return Result.Success(
                    $"Modern Wakeup {version.DriverMajor}.{version.DriverMinor} is installed and running. " +
                    $"The allowlist is at {AllowlistPath}.");
            }
            catch
            {
                if (created)
                {
                    try { CommandRunner.Run("pnputil.exe", "/remove-device", DeviceNode.InstanceId); }
                    catch { }
                }
                throw;
            }
            finally
            {
                DriverPackage.TryDelete(staging);
            }
        }
        catch (Exception exception)
        {
            return Result.Failure(exception);
        }
    }

    internal static Result Configure(IEnumerable<string> allowedApplications, string? licenseKey)
    {
        try
        {
            Platform.EnsureSupportedInstallerPlatform();
            string? license = licenseKey ?? Allowlist.ReadExistingLicense(AllowlistPath);
            string allowlist = Allowlist.Build(allowedApplications, license);
            Directory.CreateDirectory(ConfigurationDirectory);
            WriteAllowlistAtomically(allowlist);

            if (DeviceNode.Exists())
            {
                CommandResult restart = CommandRunner.Run(
                    "pnputil.exe", "/restart-device", DeviceNode.InstanceId);
                restart.EnsureSuccess("Restarting the Modern Wakeup device");
            }
            return Result.Success($"Updated {AllowlistPath}.");
        }
        catch (Exception exception)
        {
            return Result.Failure(exception);
        }
    }

    internal static Result Uninstall()
    {
        try
        {
            Platform.EnsureSupportedInstallerPlatform();
            List<string> output = [];

            if (DeviceNode.Exists())
            {
                CommandResult removeDevice = CommandRunner.Run(
                    "pnputil.exe", "/remove-device", DeviceNode.InstanceId);
                removeDevice.EnsureSuccess("Removing the Modern Wakeup device");
                AddOutput(output, removeDevice);
            }

            CommandResult stop = CommandRunner.Run("sc.exe", "stop", "ModernWakeup");
            stop.EnsureSuccess("Stopping the ModernWakeup service", 1060, 1062, 1072, 1052);
            AddOutput(output, stop);

            foreach (string publishedInf in FindPublishedInfs())
            {
                CommandResult remove = CommandRunner.Run(
                    "pnputil.exe", "/delete-driver", publishedInf, "/uninstall");
                remove.EnsureSuccess($"Removing driver package {publishedInf}");
                AddOutput(output, remove);
            }

            CommandResult delete = CommandRunner.Run("sc.exe", "delete", "ModernWakeup");
            delete.EnsureSuccess("Deleting the ModernWakeup service", 1060, 1072);
            AddOutput(output, delete);
            WaitForServiceRemoval();

            try
            {
                File.Delete(AllowlistPath);
                if (Directory.Exists(ConfigurationDirectory) &&
                    Directory.GetFileSystemEntries(ConfigurationDirectory).Length == 0)
                    Directory.Delete(ConfigurationDirectory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return Result.Success(output.Count == 0
                ? "Modern Wakeup was not installed."
                : "Modern Wakeup was uninstalled. " + string.Join(" ", output));
        }
        catch (Exception exception)
        {
            return Result.Failure(exception);
        }
    }

    internal static bool IsInstalled()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(ServiceRegistryPath, writable: false);
        return key is not null;
    }

    internal static TrialInfo ReadTrial()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(ServiceRegistryPath, writable: false);
        if (key?.GetValue(TrialStartedValueName) is not long start)
            return new TrialInfo(false, true, TrialDays, null);

        DateTimeOffset started;
        try { started = DateTimeOffset.FromFileTime(start); }
        catch (ArgumentOutOfRangeException) { return new TrialInfo(false, true, TrialDays, null); }
        if (started > DateTimeOffset.UtcNow)
            started = DateTimeOffset.UtcNow;
        DateTimeOffset expires = started.AddDays(TrialDays);
        TimeSpan remaining = expires - DateTimeOffset.UtcNow;
        int days = remaining <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(remaining.TotalDays);
        return new TrialInfo(true, days > 0, days, expires);
    }

    private static void WriteAllowlistAtomically(string contents)
    {
        string temporary = Path.Combine(
            ConfigurationDirectory, "Allowlist-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, AllowlistPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch { }
        }
    }

    private static string[] FindPublishedInfs()
    {
        string inventory = Path.Combine(
            Path.GetTempPath(), "ModernWakeup-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            CommandResult query = CommandRunner.Run(
                "pnputil.exe", "/enum-drivers", "/class", "System", "/format", "xml", "/output-file", inventory);
            query.EnsureSuccess("Reading the Windows driver-store inventory");
            XDocument document = XDocument.Load(inventory);
            return document.Root?.Elements("Driver")
                .Where(element => string.Equals(
                    (string?)element.Element("OriginalName"),
                    "ModernWakeup.Driver.inf",
                    StringComparison.OrdinalIgnoreCase))
                .Select(element => (string?)element.Attribute("DriverName"))
                .OfType<string>()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }
        finally
        {
            try { File.Delete(inventory); }
            catch { }
        }
    }

    private static void WaitForServiceRemoval()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!IsInstalled())
                return;
            Thread.Sleep(100);
        }

        throw new InvalidOperationException(
            "Windows marked the driver service for deletion, but another process still holds it open. " +
            "Close service-management tools or restart Windows to finish removal.");
    }

    private static void AddOutput(List<string> destination, CommandResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Output))
            destination.Add(result.Output.ReplaceLineEndings(" "));
    }
}

