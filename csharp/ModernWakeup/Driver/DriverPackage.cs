using System.Reflection;

namespace ModernWakeup.Driver;

internal static class DriverPackage
{
    private const string Prefix = "ModernWakeup.Driver.";
    private static readonly string[] Files =
    [
        "ModernWakeup.Driver.inf",
        "ModernWakeup.Driver.sys",
        "modernwakeup.driver.cat",
    ];

    internal static string Extract(string parentDirectory)
    {
        string directory = Path.Combine(parentDirectory, "DriverStaging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assembly assembly = typeof(Sdk).Assembly;
            foreach (string fileName in Files)
            {
                using Stream input = assembly.GetManifestResourceStream(Prefix + fileName)
                    ?? throw new InvalidOperationException($"Embedded driver resource {fileName} is missing.");
                using FileStream output = new(
                    Path.Combine(directory, fileName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
            }
            return directory;
        }
        catch
        {
            TryDelete(directory);
            throw;
        }
    }

    internal static void TryDelete(string directory)
    {
        try
        {
            if (Path.GetFileName(directory).StartsWith("DriverStaging-", StringComparison.Ordinal))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
