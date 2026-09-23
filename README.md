# Modern Wakeup SDK

Modern Wakeup is an x64 Windows compatibility driver for desktop applications that use [`SetWaitableTimer`](https://learn.microsoft.com/windows/win32/api/synchapi/nf-synchapi-setwaitabletimer) or `SetWaitableTimerEx`. On PCs that use S0 low-power idle, Windows keeps those applications suspended when their resumable timer expires. Modern Wakeup discovers approved existing timers, mirrors the earliest deadline with a wake-capable kernel timer, and requests an interactive resume when it fires.

Applications do not need to modify their code. Install the driver once, allowlist the application, and continue using the Windows API normally.


## Basic C# usage

```csharp
if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Modern Wakeup supports Windows only.");
    return 1;
}

if (!await ModernWakeup.Sdk.IsModernStandbyEnabled)
{
    Console.Error.WriteLine("S0 low-power / modern standby is not present on this system.");
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

```

## C++

The native library is dependency-free apart from the Windows SDK. It wraps handle ownership and ABI validation while leaving the C structures available in `modern_wakeup_abi.h`. The [C++ example](cpp/example/modern_wakeup_example.cpp) follows the same three-minute timer and background-diagnostics flow as the C# example.

```cpp
#include "modern_wakeup.hpp"

int wmain() {
    try {
        if (!modern_wakeup::is_modern_standby_enabled()) {
            std::cerr << "S0 low-power / modern standby is not present on this system.\n";
            return EXIT_FAILURE;
        }

        // The native SDK deliberately keeps privileged driver deployment out of
        // process. Probe the read-only device before arming the example timer.
        try {
            modern_wakeup::client probe;
            static_cast<void>(probe.get_version());
        } catch (const std::system_error& error) {
            std::cerr << "The Modern Wakeup driver is not available (" << error.what() << ").\n"
                      << "Install it from an elevated process with the C# SDK example first.\n";
            return EXIT_FAILURE;
        }

        const auto due = std::chrono::system_clock::now() + std::chrono::minutes(3);
        modern_wakeup::wake_timer timer(due, L"Modern Wakeup SDK C++ example");
        std::wcout << L"Traditional SetWaitableTimerEx timer armed for "
                   << format_time(due) << L".\n"
                   << L"Keep this process running and put the PC into Modern Standby.\n";

        std::jthread diagnostics(run_diagnostics);

        timer.wait();
        diagnostics.request_stop();
        diagnostics.join();
        std::wcout << L"Timer fired at "
                   << format_time(std::chrono::system_clock::now()) << L".\n";
        return EXIT_SUCCESS;
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        return EXIT_FAILURE;
    }
}
```


## How it works

1. An application creates a resumable Windows waitable timer.
2. The driver reads the kernel wake-timer list and requester metadata.
3. It applies the local allowlist and selects the earliest approved future timer.
4. It mirrors that deadline with a wake-capable kernel timer using zero no-wake tolerance.
5. At expiry it requests system, display, and user-present activity so Windows resumes interactively.
6. The application's original timer becomes observable; the driver does not invoke application code.


## Allowlist and licensing

The active UTF-8 file is `%ProgramData%\ModernWakeup\ModernWakeup-Allowed-Timers.txt`. Each nonempty, non-comment line is a case-insensitive wildcard rule. Rules match a user process's full image path and base name, or a kernel requester's device description and path.

```text
MyRecorder.exe
MyRecorder.Service*.exe
C:\Program Files\Example\*.exe

// License Key: <paste your license key here>
```

- `*` matches every user and kernel requester. It is convenient for a quick experiment but too broad for most deployments.
- `*.exe` generally permits user applications but not kernel requesters.
- Missing, empty, unreadable, oversized, or nonmatching configuration fails closed.
- `ConfigureAllowedApps` preserves a valid-looking existing license line unless a replacement is supplied.

The signed driver provides a seven-day machine trial beginning at installation. After trial expiry, mirroring needs a valid Modern Wakeup license. The allowlist always applies. Licensing is verified inside the driver and cannot be bypassed through this SDK.

## Public ABI

The read-only device is `\\.\ModernWakeup`, currently at ABI version 2. Its four buffered IOCTLs return the driver version, latest event, a cancellable next event, and the currently mirrored timer. Structures use fixed-width, pointer-free fields; timestamps are UTC Windows FILETIME values (100-nanosecond ticks since 1601).

There is no IOCTL for arming or cancelling the driver's mirror. That is intentional: Modern Wakeup derives its state exclusively from Windows' existing timer list, so installed applications remain conventional Windows applications.

See [ABI reference](docs/abi.md) and [the exact C header](cpp/include/modern_wakeup_abi.h).

## WHCP-certified Driver

`driver/` contains a INF/SYS/CAT package. All three files currently validate as signed by:

> Microsoft Windows Hardware Compatibility Publisher

Published SHA-256 values are in [driver/SHA256SUMS.txt](driver/SHA256SUMS.txt).

## Important limitations

- Wake behavior still depends on Windows policy, firmware, networking mode, battery state, and hardware. A wake can occur later than its requested deadline, 1 - 3 minutes even.
- The bridge relies on kernel wake-timer information that is available to driver code but is not a stable application-level compatibility contract.
- Modern Wakeup requests system, display, and user-present activity when its mirrored timer fires. The resulting display-on resume disengages the Desktop Activity Moderator (DAM), allowing suspended desktop applications to run and observe their original timers. Normal Windows power policy may turn the display off and re-enter Modern Standby afterward.
- Timers created or changed after the driver's last scan are discovered on its next relevant power/display transition.
- The driver must be installed and running when the deadline expires, and the original application must retain its waitable-timer handle.

## License

Except where stated otherwise, the Modern Wakeup SDK source code, headers, examples, and documentation are licensed under the MIT License.

The signed Modern Wakeup driver files under `driver/`, including copies embedded in distributed assemblies or packages, are proprietary redistributable components and are not licensed under MIT. Runtime use after the trial period requires a valid [Modern Wakeup license](https://modernwakeup.com). See [DRIVER-LICENSE.md](DRIVER-LICENSE.md).
