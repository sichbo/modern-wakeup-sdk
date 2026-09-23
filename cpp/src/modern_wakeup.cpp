#define _WIN32_WINNT 0x0602

#include "modern_wakeup.hpp"

#include <PowrProf.h>
#include <cstddef>
#include <stdexcept>
#include <system_error>
#include <utility>

namespace modern_wakeup {
namespace {

static_assert(sizeof(SYSTEM_POWER_CAPABILITIES) == 76);
static_assert(offsetof(SYSTEM_POWER_CAPABILITIES, AoAc) == 20);

[[noreturn]] void throw_last_error(const char* operation) {
    throw std::system_error(static_cast<int>(GetLastError()), std::system_category(), operation);
}

template <typename T>
T ioctl(HANDLE handle, DWORD code, void* input = nullptr, DWORD input_size = 0) {
    T value{};
    DWORD returned = 0;
    if (!DeviceIoControl(handle, code, input, input_size, &value, sizeof(value), &returned, nullptr))
        throw_last_error("Modern Wakeup DeviceIoControl");
    if (returned != sizeof(value) || value.Size != sizeof(value) ||
        value.AbiVersion != MODERN_WAKEUP_ABI_VERSION)
        throw std::runtime_error("The Modern Wakeup driver returned an incompatible ABI structure.");
    return value;
}

constexpr std::int64_t file_time_unix_epoch = 116444736000000000LL;
constexpr std::int64_t ticks_per_second = 10000000LL;

} // namespace

client::client() {
    handle_ = CreateFileW(
        device_path,
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
    if (handle_ == INVALID_HANDLE_VALUE)
        throw_last_error("Open \\\\.\\ModernWakeup");
}

client::~client() {
    if (handle_ != INVALID_HANDLE_VALUE)
        CloseHandle(handle_);
}

client::client(client&& other) noexcept : handle_(std::exchange(other.handle_, INVALID_HANDLE_VALUE)) {}

client& client::operator=(client&& other) noexcept {
    if (this != &other) {
        if (handle_ != INVALID_HANDLE_VALUE)
            CloseHandle(handle_);
        handle_ = std::exchange(other.handle_, INVALID_HANDLE_VALUE);
    }
    return *this;
}

version client::get_version() const {
    const auto native = ioctl<MODERN_WAKEUP_VERSION>(handle_, IOCTL_MODERN_WAKEUP_GET_VERSION);
    return {native.AbiVersion, native.DriverMajor, native.DriverMinor};
}

MODERN_WAKEUP_EVENT client::get_last_event() const {
    return ioctl<MODERN_WAKEUP_EVENT>(handle_, IOCTL_MODERN_WAKEUP_GET_LAST_EVENT);
}

MODERN_WAKEUP_EVENT client::wait_event(std::uint64_t after_sequence) const {
    MODERN_WAKEUP_WAIT_INPUT input{sizeof(input), 0, after_sequence};
    return ioctl<MODERN_WAKEUP_EVENT>(
        handle_, IOCTL_MODERN_WAKEUP_WAIT_EVENT, &input, sizeof(input));
}

MODERN_WAKEUP_TIMER_INFO client::get_wake_timer_info() const {
    auto value = ioctl<MODERN_WAKEUP_TIMER_INFO>(handle_, IOCTL_MODERN_WAKEUP_GET_WAKE_TIMER_INFO);
    if (value.Reserved != 0)
        throw std::runtime_error("The Modern Wakeup driver returned non-zero reserved data.");
    return value;
}

void client::cancel_pending_io() const noexcept {
    if (handle_ != INVALID_HANDLE_VALUE)
        CancelIoEx(handle_, nullptr);
}

wake_timer::wake_timer(
    std::chrono::system_clock::time_point due_time,
    const std::wstring& reason) {
    if (due_time <= std::chrono::system_clock::now())
        throw std::invalid_argument("Wake time must be in the future.");
    if (reason.empty())
        throw std::invalid_argument("Supply a diagnostic reason for the wake timer.");

    handle_ = CreateWaitableTimerExW(
        nullptr, nullptr, CREATE_WAITABLE_TIMER_MANUAL_RESET,
        TIMER_MODIFY_STATE | SYNCHRONIZE);
    if (!handle_)
        throw_last_error("CreateWaitableTimerExW");

    const auto unix_ticks = std::chrono::duration_cast<std::chrono::duration<std::int64_t, std::ratio<1, 10000000>>>(
        due_time.time_since_epoch()).count();
    LARGE_INTEGER due{};
    due.QuadPart = unix_ticks + file_time_unix_epoch;
    REASON_CONTEXT context{};
    context.Version = POWER_REQUEST_CONTEXT_VERSION;
    context.Flags = POWER_REQUEST_CONTEXT_SIMPLE_STRING;
    context.Reason.SimpleReasonString = const_cast<PWSTR>(reason.c_str());
    if (!SetWaitableTimerEx(handle_, &due, 0, nullptr, nullptr, &context, 0)) {
        const DWORD error = GetLastError();
        CloseHandle(handle_);
        handle_ = nullptr;
        SetLastError(error);
        throw_last_error("SetWaitableTimerEx");
    }
}

wake_timer::~wake_timer() {
    if (handle_) {
        CancelWaitableTimer(handle_);
        CloseHandle(handle_);
    }
}

void wake_timer::wait() const {
    const DWORD result = WaitForSingleObject(handle_, INFINITE);
    if (result != WAIT_OBJECT_0)
        throw_last_error("WaitForSingleObject");
}

void wake_timer::cancel() const {
    if (!CancelWaitableTimer(handle_))
        throw_last_error("CancelWaitableTimer");
}

bool is_modern_standby_enabled() {
    SYSTEM_POWER_CAPABILITIES capabilities{};
    if (!GetPwrCapabilities(&capabilities))
        throw_last_error("GetPwrCapabilities");
    return capabilities.AoAc != FALSE;
}

std::chrono::system_clock::time_point from_file_time(std::int64_t value) {
    const auto ticks = value - file_time_unix_epoch;
    return std::chrono::system_clock::time_point(
        std::chrono::duration_cast<std::chrono::system_clock::duration>(
            std::chrono::duration<std::int64_t, std::ratio<1, ticks_per_second>>(ticks)));
}

} // namespace modern_wakeup
