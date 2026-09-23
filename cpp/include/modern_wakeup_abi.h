#pragma once

// Modern Wakeup IOCTL ABI version 2.

#include <Windows.h>
#include <winioctl.h>

#define MODERN_WAKEUP_DEVICE_TYPE 0x8000u

#define IOCTL_MODERN_WAKEUP_GET_VERSION \
    CTL_CODE(MODERN_WAKEUP_DEVICE_TYPE, 0x800u, METHOD_BUFFERED, FILE_READ_DATA)

#define IOCTL_MODERN_WAKEUP_GET_LAST_EVENT \
    CTL_CODE(MODERN_WAKEUP_DEVICE_TYPE, 0x801u, METHOD_BUFFERED, FILE_READ_DATA)

#define IOCTL_MODERN_WAKEUP_WAIT_EVENT \
    CTL_CODE(MODERN_WAKEUP_DEVICE_TYPE, 0x802u, METHOD_BUFFERED, FILE_READ_DATA)

#define IOCTL_MODERN_WAKEUP_GET_WAKE_TIMER_INFO \
    CTL_CODE(MODERN_WAKEUP_DEVICE_TYPE, 0x803u, METHOD_BUFFERED, FILE_READ_DATA)

#define MODERN_WAKEUP_ABI_VERSION 2u
#define MODERN_WAKEUP_DIAGNOSTIC_TEXT_CHARS 512u

typedef struct _MODERN_WAKEUP_VERSION {
    ULONG Size;
    ULONG AbiVersion;
    ULONG DriverMajor;
    ULONG DriverMinor;
} MODERN_WAKEUP_VERSION, *PMODERN_WAKEUP_VERSION;

typedef enum _MODERN_WAKEUP_EVENT_SOURCE {
    ModernWakeupEventSourceNone = 0,
    ModernWakeupEventSourceConsoleDisplay = 1,
    ModernWakeupEventSourceLegacyWakeTimer = 2,
    ModernWakeupEventSourceStatusMessage = 3
} MODERN_WAKEUP_EVENT_SOURCE;

typedef enum _MODERN_WAKEUP_STATUS_MESSAGE {
    ModernWakeupStatusInvalidLicense = 1,
    ModernWakeupStatusTimerSelected = 2,
    ModernWakeupStatusTimerExecuted = 3,
    ModernWakeupStatusTimerCleared = 4,
    ModernWakeupStatusValidLicense = 5
} MODERN_WAKEUP_STATUS_MESSAGE;

typedef enum _MODERN_WAKEUP_CONSOLE_DISPLAY_STATE {
    ModernWakeupDisplayOff = 0,
    ModernWakeupDisplayOn = 1,
    ModernWakeupDisplayDimmed = 2,
    ModernWakeupDisplayUnknown = 0xffffffffu
} MODERN_WAKEUP_CONSOLE_DISPLAY_STATE;

typedef struct _MODERN_WAKEUP_EVENT {
    ULONG Size;
    ULONG AbiVersion;
    ULONGLONG Sequence;
    LONGLONG Timestamp100nsUtc;
    ULONG Source;
    ULONG Value;
} MODERN_WAKEUP_EVENT, *PMODERN_WAKEUP_EVENT;

typedef struct _MODERN_WAKEUP_WAIT_INPUT {
    ULONG Size;
    ULONG Reserved;
    ULONGLONG AfterSequence;
} MODERN_WAKEUP_WAIT_INPUT, *PMODERN_WAKEUP_WAIT_INPUT;

typedef struct _MODERN_WAKEUP_TIMER_INFO {
    ULONG Size;
    ULONG AbiVersion;
    LONGLONG DueTime100nsUtc;
    ULONG Armed;
    ULONG PeriodMilliseconds;
    ULONG CallerType;
    ULONG ProcessId;
    ULONG ServiceTag;
    ULONG Reserved;
    WCHAR ProcessImageName[MODERN_WAKEUP_DIAGNOSTIC_TEXT_CHARS];
    WCHAR DeviceDescription[MODERN_WAKEUP_DIAGNOSTIC_TEXT_CHARS];
    WCHAR DevicePath[MODERN_WAKEUP_DIAGNOSTIC_TEXT_CHARS];
    WCHAR Reason[MODERN_WAKEUP_DIAGNOSTIC_TEXT_CHARS];
} MODERN_WAKEUP_TIMER_INFO, *PMODERN_WAKEUP_TIMER_INFO;

#ifdef __cplusplus
static_assert(sizeof(MODERN_WAKEUP_VERSION) == 16);
static_assert(sizeof(MODERN_WAKEUP_EVENT) == 32);
static_assert(sizeof(MODERN_WAKEUP_WAIT_INPUT) == 16);
static_assert(sizeof(MODERN_WAKEUP_TIMER_INFO) == 4136);
#endif
