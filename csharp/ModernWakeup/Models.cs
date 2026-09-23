namespace ModernWakeup;

/// <summary>The result of a driver installation, configuration, or removal operation.</summary>
public sealed record Result(bool Succeeded, string Message, int? NativeErrorCode = null)
{
    internal static Result Success(string message) => new(true, message);

    internal static Result Failure(Exception exception)
    {
        int? nativeCode = exception switch
        {
            System.ComponentModel.Win32Exception win32 => win32.NativeErrorCode,
            ModernWakeupException modern => modern.NativeErrorCode,
            _ => null,
        };
        return new Result(false, exception.Message, nativeCode);
    }

    /// <summary>Throws when the operation did not succeed.</summary>
    public void EnsureSuccess()
    {
        if (!Succeeded)
            throw new ModernWakeupException(Message, NativeErrorCode);
    }
}

/// <summary>Describes the installed driver and this PC's relevant power capability.</summary>
public sealed record ModernWakeupStatus(
    bool IsModernStandbyEnabled,
    bool IsDriverInstalled,
    bool IsDriverAvailable,
    string AllowlistPath,
    bool AllowlistExists,
    ModernWakeupVersion? DriverVersion,
    TrialInfo Trial);

/// <summary>Driver and public ABI version information.</summary>
public readonly record struct ModernWakeupVersion(uint AbiVersion, uint DriverMajor, uint DriverMinor)
{
    public override string ToString() => $"{DriverMajor}.{DriverMinor} (ABI {AbiVersion})";
}

/// <summary>The driver event source.</summary>
public enum WakeupEventSource : uint
{
    None = 0,
    ConsoleDisplay = 1,
    LegacyWakeTimer = 2,
    StatusMessage = 3,
}

/// <summary>A stable driver status-message identifier.</summary>
public enum WakeupStatusMessage : uint
{
    InvalidLicense = 1,
    TimerSelected = 2,
    TimerExecuted = 3,
    TimerCleared = 4,
    ValidLicense = 5,
    TrialWakeOneDayRemaining = 6,
    TrialWakeTwoDaysRemaining = 7,
    TrialWakeThreeDaysRemaining = 8,
    TrialWakeFourDaysRemaining = 9,
    TrialWakeFiveDaysRemaining = 10,
    TrialWakeSixDaysRemaining = 11,
    TrialWakeSevenDaysRemaining = 12,
    TrialWakeExpired = 13,
}

/// <summary>The kind of requester that owns a mirrored Windows wake timer.</summary>
public enum WakeTimerCallerType : uint
{
    Kernel = 0,
    UserProcess = 1,
    UserSharedService = 2,
}

/// <summary>One event emitted through the driver's read-only diagnostics ABI.</summary>
public sealed record WakeupEvent(
    ulong Sequence,
    DateTimeOffset Timestamp,
    WakeupEventSource Source,
    uint Value)
{
    public WakeupStatusMessage? StatusMessage => Source == WakeupEventSource.StatusMessage
        ? (WakeupStatusMessage)Value
        : null;
}

/// <summary>Details of the earliest currently mirrored allowlisted timer.</summary>
public sealed record WakeTimerInfo(
    bool Armed,
    DateTimeOffset? DueTime,
    uint PeriodMilliseconds,
    WakeTimerCallerType CallerType,
    uint ProcessId,
    uint ServiceTag,
    string ProcessImageName,
    string DeviceDescription,
    string DevicePath,
    string Reason);

/// <summary>Machine trial state recorded by the driver installation.</summary>
public sealed record TrialInfo(bool Started, bool Active, int DaysRemaining, DateTimeOffset? ExpiresAt);

/// <summary>Options used to install and configure the signed driver package.</summary>
public sealed class InstallOptions
{
    /// <summary>Process names, paths, device descriptions, or wildcard patterns that may wake the PC.</summary>
    public required IEnumerable<string> AllowedApplications { get; init; }

    /// <summary>An optional Modern Wakeup license key written to the allowlist comment. A license can be obtained at modernwakeup.com.</summary>
    public string? LicenseKey { get; init; }
}

/// <summary>An error reported by the SDK.</summary>
public sealed class ModernWakeupException : Exception
{
    public ModernWakeupException(string message, int? nativeErrorCode = null, Exception? innerException = null)
        : base(message, innerException) => NativeErrorCode = nativeErrorCode;

    public int? NativeErrorCode { get; }
}
