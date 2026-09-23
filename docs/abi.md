# Public driver ABI

Modern Wakeup exposes a read-only control device at `\\.\ModernWakeup`. Open it with `GENERIC_READ` and `FILE_SHARE_READ | FILE_SHARE_WRITE`. All operations use `METHOD_BUFFERED` and require `FILE_READ_DATA`.

ABI version: **2**

| Function | IOCTL | Input | Output |
| --- | --- | --- | --- |
| `0x800` | `IOCTL_MODERN_WAKEUP_GET_VERSION` | none | `MODERN_WAKEUP_VERSION` |
| `0x801` | `IOCTL_MODERN_WAKEUP_GET_LAST_EVENT` | none | `MODERN_WAKEUP_EVENT` |
| `0x802` | `IOCTL_MODERN_WAKEUP_WAIT_EVENT` | `MODERN_WAKEUP_WAIT_INPUT` | `MODERN_WAKEUP_EVENT` |
| `0x803` | `IOCTL_MODERN_WAKEUP_GET_WAKE_TIMER_INFO` | none | `MODERN_WAKEUP_TIMER_INFO` |

Every output structure starts with `Size` and `AbiVersion`. A client should reject a structure when either differs from the version it was compiled against. Reserved fields must be zero. The exact declarations and compile-time size checks live in [`modern_wakeup_abi.h`](../cpp/include/modern_wakeup_abi.h).

## Events

`GET_LAST_EVENT` is a nonblocking snapshot. `WAIT_EVENT` accepts an `AfterSequence` cursor and completes immediately when the current sequence differs, or remains pending until the next event. Use a separate device handle for a pending wait if the same process also makes snapshot queries. Cancel pending I/O before closing a monitoring component.

| Source | Value |
| --- | --- |
| `None` (`0`) | No event has been published. |
| `ConsoleDisplay` (`1`) | `0` off, `1` on, `2` dimmed, `0xffffffff` unknown. |
| `LegacyWakeTimer` (`2`) | Reserved for legacy-wake notifications. |
| `StatusMessage` (`3`) | Stable message ID below. |

Status IDs `1`–`5` report invalid license, timer selected, timer executed, timer cleared, and valid license. IDs `6`–`12` are trial-wake warnings with one through seven days remaining. ID `13` reports the final delayed wake after trial expiry.

## Timer snapshot

`GET_WAKE_TIMER_INFO` returns the earliest allowed timer currently mirrored by the driver. `Armed == 0` means no timer is mirrored. `DueTime100nsUtc` and event timestamps are UTC Windows FILETIME values. Text arrays are null-terminated UTF-16 and each have room for 512 code units.

Current caller types are `0` kernel, `1` user process, and `2` shared-service process. Depending on caller type, requester data appears in `ProcessImageName`, `DeviceDescription`, and `DevicePath`. `Reason` is populated when the originating application supplied a simple `REASON_CONTEXT`, as the SDK examples do.

The ABI is diagnostic only. Applications create ordinary resumable Windows waitable timers; the driver chooses and maintains its kernel mirror without accepting deadlines from user mode.
