#include "modern_wakeup.hpp"

#include <chrono>
#include <cstdlib>
#include <ctime>
#include <iomanip>
#include <iostream>
#include <mutex>
#include <sstream>
#include <stop_token>
#include <string>
#include <system_error>
#include <thread>

namespace {

std::mutex output_mutex;

std::wstring format_time(std::chrono::system_clock::time_point value) {
    const auto seconds = std::chrono::system_clock::to_time_t(value);
    std::tm local{};
    localtime_s(&local, &seconds);

    const auto milliseconds = std::chrono::duration_cast<std::chrono::milliseconds>(
        value.time_since_epoch()) % 1000;
    std::wostringstream output;
    output << std::put_time(&local, L"%Y-%m-%dT%H:%M:%S")
           << L'.' << std::setfill(L'0') << std::setw(3) << milliseconds.count();
    return output.str();
}

const wchar_t* describe_source(ULONG source) {
    switch (source) {
    case ModernWakeupEventSourceNone:
        return L"None";
    case ModernWakeupEventSourceConsoleDisplay:
        return L"ConsoleDisplay";
    case ModernWakeupEventSourceLegacyWakeTimer:
        return L"LegacyWakeTimer";
    case ModernWakeupEventSourceStatusMessage:
        return L"StatusMessage";
    default:
        return L"Unknown";
    }
}

std::wstring describe_event(const MODERN_WAKEUP_EVENT& event) {
    if (event.Source != ModernWakeupEventSourceStatusMessage)
        return L"value=" + std::to_wstring(event.Value);

    switch (event.Value) {
    case 1:
        return L"The driver could not validate a license - a license can be obtained at modernwakeup.com; wake-timer mirroring is disabled.";
    case 2:
        return L"The driver selected and mirrored an allowed wake timer.";
    case 3:
        return L"The driver executed its scheduled wakeup.";
    case 4:
        return L"The driver cleared its mirror because no allowed wake timer remains.";
    case 5:
        return L"The driver validated its license.";
    case 13:
        return L"The delayed trial wake executed after expiry; future wakeups require a license.";
    default:
        if (event.Value >= 6 && event.Value <= 12)
            return L"The driver woke the system using its trial; " +
                std::to_wstring(event.Value - 5) + L" day(s) remain.";
        return L"Unknown Modern Wakeup status message " + std::to_wstring(event.Value) + L'.';
    }
}

void print_event(const MODERN_WAKEUP_EVENT& event) {
    std::lock_guard lock(output_mutex);
    std::wcout << L'#' << event.Sequence << L' ';
    if (event.Timestamp100nsUtc > 0)
        std::wcout << format_time(modern_wakeup::from_file_time(event.Timestamp100nsUtc));
    else
        std::wcout << L"n/a";
    std::wcout << L' ' << describe_source(event.Source) << L": "
               << describe_event(event) << L'\n';
}

void run_diagnostics(std::stop_token stop) noexcept {
    try {
        modern_wakeup::client client;
        std::stop_callback cancel_wait(stop, [&client] { client.cancel_pending_io(); });
        const auto version = client.get_version();
        const auto timer = client.get_wake_timer_info();
        const auto last = client.get_last_event();

        {
            std::lock_guard lock(output_mutex);
            std::wcout << L"Driver " << version.driver_major << L'.' << version.driver_minor
                       << L" (ABI " << version.abi << L")\n";
            if (timer.Armed) {
                std::wcout << L"Mirroring " << timer.ProcessImageName << L" at "
                           << format_time(modern_wakeup::from_file_time(timer.DueTime100nsUtc));
                if (timer.Reason[0] != L'\0')
                    std::wcout << L": " << timer.Reason;
                std::wcout << L'\n';
            } else {
                std::wcout << L"No allowed wake timer is currently mirrored.\n";
            }
            std::wcout << L"Last event: ";
        }
        print_event(last);

        auto sequence = last.Sequence;
        while (!stop.stop_requested()) {
            const auto event = client.wait_event(sequence);
            sequence = event.Sequence;
            print_event(event);
        }
    } catch (const std::exception& error) {
        if (stop.stop_requested())
            return;
        std::lock_guard lock(output_mutex);
        std::cerr << "Diagnostics stopped: " << error.what() << '\n';
    }
}

} // namespace

int wmain() {
    try {
        if (!modern_wakeup::is_modern_standby_enabled()) {
            std::cerr << "S0 low-power idle / modern standby is not present on this system.\n";
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
