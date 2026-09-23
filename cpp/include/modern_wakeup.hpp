#pragma once

#include "modern_wakeup_abi.h"

#include <chrono>
#include <cstdint>
#include <string>

namespace modern_wakeup {

inline constexpr wchar_t device_path[] = LR"(\\.\ModernWakeup)";

struct version {
    std::uint32_t abi;
    std::uint32_t driver_major;
    std::uint32_t driver_minor;
};

class client final {
public:
    client();
    ~client();
    client(const client&) = delete;
    client& operator=(const client&) = delete;
    client(client&& other) noexcept;
    client& operator=(client&& other) noexcept;

    [[nodiscard]] version get_version() const;
    [[nodiscard]] MODERN_WAKEUP_EVENT get_last_event() const;
    [[nodiscard]] MODERN_WAKEUP_EVENT wait_event(std::uint64_t after_sequence) const;
    [[nodiscard]] MODERN_WAKEUP_TIMER_INFO get_wake_timer_info() const;
    void cancel_pending_io() const noexcept;

private:
    HANDLE handle_{INVALID_HANDLE_VALUE};
};

class wake_timer final {
public:
    wake_timer(std::chrono::system_clock::time_point due_time, const std::wstring& reason);
    ~wake_timer();
    wake_timer(const wake_timer&) = delete;
    wake_timer& operator=(const wake_timer&) = delete;

    void wait() const;
    void cancel() const;

private:
    HANDLE handle_{nullptr};
};

[[nodiscard]] bool is_modern_standby_enabled();
[[nodiscard]] std::chrono::system_clock::time_point from_file_time(std::int64_t value);

} // namespace modern_wakeup
