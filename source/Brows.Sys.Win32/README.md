# Brows.Sys.Win32

The Windows backend for Brows system notifications. This package targets
`net10.0`, references `Brows.Sys`, and is marked as supported on Windows.
It contains native declarations and an internal decoder for `WM_DEVICECHANGE`.

## Installation and integration

```powershell
dotnet add package Brows.Sys.Win32
```

The native declarations and decoder are internal implementation details. This
package does not expose a standalone public listener or public P/Invoke wrapper.
Applications normally use `Brows.Sys.Win32.Windows`, which references this
backend and installs hooks on WPF window sources. The backend itself does not
register or hook a window.

## Supported notifications

| Native event | Typed message |
| --- | --- |
| `DBT_DEVNODES_CHANGED` | `DeviceTreeChange` |
| `DBT_DEVICEARRIVAL` | `DeviceArrival` |
| `DBT_DEVICEQUERYREMOVE` | `DeviceRemovalRequest` |
| `DBT_DEVICEQUERYREMOVEFAILED` | `DeviceRemovalFailed` |
| `DBT_DEVICEREMOVEPENDING` | `DeviceRemovalPending` |
| `DBT_DEVICEREMOVECOMPLETE` | `DeviceRemovalComplete` |

For `DBT_DEVTYP_VOLUME` payloads, the decoder:

- Validates the declared header and volume-record sizes before reading the
  corresponding fields.
- Creates one message per affected drive bit from A through Z.
- Supplies a `VolumeDevice` whose `VolumeName` is the drive letter without a colon or
  trailing backslash.
- Maps `DBTF_MEDIA` and `DBTF_NET` independently to `VolumeFlag.Media` and
  `VolumeFlag.Network`.

For `DBT_DEVTYP_PORT`, the decoder copies a bounded Unicode name into `PortDevice.PortName`.
For `DBT_DEVTYP_DEVICEINTERFACE`, it copies the interface class GUID and bounded
Unicode name into `InterfaceDevice`. Both payload types reuse all five change records.

`DBT_DEVNODES_CHANGED` is decoded before any pointer reads. It emits one
`DeviceTreeChange` and does not identify an individual device.

Recognized OEM and handle events retain the null-device fallback. Undefined device
types and unsupported event codes are ignored. Null/truncated payloads and names
without a UTF-16 terminator inside the declared size are ignored; topology events
need no payload. All identifiers are copied before publication.

## Scope

The package does not currently publish file-system changes, Shell change
notifications or handle-specific device information.
The native declarations in `Win32/PlatformInvoke/` include selected kernel32
functions, but their presence does not provide a public file-watching API.

The decoder checks the size advertised by the sender. It cannot establish that
an arbitrary nonzero native pointer is readable; incoming message data must
follow the native Windows contract. Native payloads are read synchronously so
the returned managed records contain copied values.

For supported device changes, consumption uses the contracts and records from
`Brows.Sys` and the listener supplied by `Brows.Sys.Win32.Windows`.

## License and source

[MIT license](https://licenses.nuget.org/MIT).
[Source repository](https://github.com/brows-app/brows-sys).
