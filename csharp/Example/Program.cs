
if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Modern Wakeup supports Windows only.");
    return 1;
}

if (!await ModernWakeup.Sdk.IsModernStandbyEnabled)
{
    Console.Error.WriteLine("S0 low-power idle / modern standby is not present on this system.");
    return 1;
}

var status = await ModernWakeup.Sdk.GetStatusAsync();
if (!status.IsDriverInstalled)
{
    Print(await ModernWakeup.Sdk.InstallDriver(new ModernWakeup.InstallOptions()
    {
        AllowedApplications = [Path.GetFileName(Environment.ProcessPath) ?? "ModernWakeup.Example.exe"]
    }));
}

var due = DateTimeOffset.Now.AddMinutes(3);
using var timer = ModernWakeup.Sdk.CreateWakeTimer(due, "Modern Wakeup SDK C# example");
Console.WriteLine($"Traditional SetWaitableTimerEx timer armed for {due:O}.");
Console.WriteLine("Keep this process running and put the PC into Modern Standby.");
_ = RunDiagnostics(); // run diagnostics in background
await timer.WaitAsync();
Console.WriteLine($"Timer fired at {DateTimeOffset.Now:O}.");

return 0;

static async Task RunDiagnostics()
{
    using var client = ModernWakeup.ModernWakeupClient.Open();
    Console.WriteLine($"Driver {client.GetVersion()}");
    var timer = client.GetWakeTimerInfo();
    Console.WriteLine(timer.Armed
        ? $"Mirroring {timer.ProcessImageName} at {timer.DueTime:O}: {timer.Reason}"
        : "No allowed wake timer is currently mirrored.");

    var last = client.GetLastEvent();
    Console.WriteLine($"Last event: #{last.Sequence} {last.Timestamp:O} {last.Source} value={last.Value}");
    Console.WriteLine("Watching events; press Ctrl+C to stop.");
    using CancellationTokenSource stop = new();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        stop.Cancel();
    };
    try
    {
        await foreach (var wakeEvent in client.WatchEventsAsync(last.Sequence, stop.Token))
        {
            string detail = wakeEvent.StatusMessage is { } message
                ? ModernWakeup.ModernWakeupClient.DescribeStatusMessage(message)
                : $"value={wakeEvent.Value}";
            Console.WriteLine($"#{wakeEvent.Sequence} {wakeEvent.Timestamp:O} {wakeEvent.Source}: {detail}");
        }
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
}

static void Print(ModernWakeup.Result result)
{
    Console.WriteLine(result.Message);
    result.EnsureSuccess();
}
