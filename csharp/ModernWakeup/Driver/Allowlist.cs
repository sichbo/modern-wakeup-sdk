using System.Text;

namespace ModernWakeup.Driver;

internal static class Allowlist
{
    private const int MaximumBytes = 64 * 1024;
    private const string LicensePrefix = "// License Key:";

    internal static string Build(IEnumerable<string> allowedApplications, string? licenseKey)
    {
        ArgumentNullException.ThrowIfNull(allowedApplications);
        List<string> patterns = [];
        foreach (string? candidate in allowedApplications)
        {
            if (candidate is null)
                throw new ArgumentException("Allowlist entries cannot be null.", nameof(allowedApplications));

            string pattern = candidate.Trim();
            if (pattern.Length == 0)
                continue;
            if (pattern.Contains('\r') || pattern.Contains('\n'))
                throw new ArgumentException("Each allowlist entry must be a single line.", nameof(allowedApplications));
            if (pattern.Contains("//", StringComparison.Ordinal))
                throw new ArgumentException("Allowlist entries cannot contain the // comment marker.", nameof(allowedApplications));
            if (!patterns.Contains(pattern, StringComparer.OrdinalIgnoreCase))
                patterns.Add(pattern);
        }

        string normalizedLicense = (licenseKey ?? string.Empty).Trim();
        if (normalizedLicense.Contains('\r') || normalizedLicense.Contains('\n'))
            throw new ArgumentException("The license key must be a single line.", nameof(licenseKey));

        StringBuilder builder = new();
        builder.AppendLine("// Modern Wakeup allowed timer requesters.");
        builder.AppendLine("// Wildcards are supported; matching is case-insensitive.");
        foreach (string pattern in patterns)
            builder.AppendLine(pattern);
        builder.AppendLine();
        builder.Append(LicensePrefix).Append(' ');
        if (normalizedLicense.Length == 0)
            builder.Append("<paste your license key here>");
        else
            builder.Append(normalizedLicense);
        builder.AppendLine();

        string contents = builder.ToString();
        if (Encoding.UTF8.GetByteCount(contents) > MaximumBytes)
            throw new ArgumentException("The generated allowlist exceeds the driver's 64 KiB limit.", nameof(allowedApplications));
        return contents;
    }

    internal static string? ReadExistingLicense(string path)
    {
        if (!File.Exists(path))
            return null;

        foreach (string line in File.ReadLines(path, Encoding.UTF8))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith(LicensePrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            string value = trimmed[LicensePrefix.Length..].Trim();
            return value.Length == 0 || value.StartsWith('<') ? null : value;
        }
        return null;
    }
}

