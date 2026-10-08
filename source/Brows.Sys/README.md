# Brows.Sys

Message contracts and immutable device-change and clipboard-change records for
the Brows system notification libraries. This package targets `net10.0` and
contains no native window-hook implementation.

## Installation

```powershell
dotnet add package Brows.Sys
```

Use this package when consuming typed messages.
For the supplied Windows implementation, use `Brows.Sys.Win32.Windows`, which
references these contracts transitively.

## Public contracts

| Type | Purpose |
| --- | --- |
| `ISystemMessage` / `SystemMessage` | Identify a message's `SystemMessageKind`; `SystemMessage` is the base record. |
| `SystemMessageEventArgs` | Carries an `ISystemMessage` in its `Message` property. |
| `DeviceMessage` / `DeviceChange` | Carry a device and identify its change kind. |
| `VolumeDevice` | Identifies a logical drive and its media/network flags. |
| `PortDevice` | Copies the friendly port or connected-device name. |
| `InterfaceDevice` | Copies the interface class GUID and native interface name. |
| `DeviceTreeChange` | Signals that consumers should refresh their device inventory. |
| `ClipboardChange` | Signals a clipboard change and carries a sequence-number state hint. |

Contracts are in `Brows.Sys`. The `ClipboardChange` message is in
`Brows.Sys.Messages.ClipboardMessages`. Device models are under
`Brows.Sys.Messages` and `Brows.Sys.Messages.DeviceMessages`. Concrete
change records are in `Brows.Sys.Messages.DeviceMessages.DeviceChanges`;
the volume model is in `Brows.Sys.Messages.DeviceMessages.Devices`.

`ISystemMessenger` is internal. Messenger implementations and creation are
reserved for libraries in this repository. Applications consume notifications
through the public `ISystemMessengerSet` in `Brows.Sys.Composition`.

`SystemMessageKind.None=0` represents an unspecified notification category and is
the default enum value. `SystemMessageKind.Device=1` identifies device notifications,
and `SystemMessageKind.Clipboard=2` identifies clipboard notifications.
`None` does not represent an emitted notification or the absence of clipboard contents.
`DeviceMessageKind.TreeChange` identifies
`DeviceTreeChange`, whose inherited `Device` is null because no individual device
is identified. Consumers should enumerate devices initially and refresh their inventory
when this notification arrives; notifications do not provide an initial snapshot.

`DeviceKind` preserves `None=0` and `Volume=1`, and adds `Port=2` and `Interface=3`.
`DeviceMessageKind.Change=0` is preserved and `TreeChange=1` is appended.

Device changes are represented by
`DeviceArrival`, `DeviceRemovalRequest`, `DeviceRemovalFailed`,
`DeviceRemovalPending`, and `DeviceRemovalComplete` records.

## Example

```csharp
using Brows.Sys.Messages.DeviceMessages.DeviceChanges;
using Brows.Sys.Messages.DeviceMessages.Devices;
using System;

var message = new DeviceArrival {
    Device = new VolumeDevice {
        VolumeName = "E",
        VolumeFlag = VolumeFlag.Media,
    },
};

Console.WriteLine($"{message.DeviceChangeKind}: {message.Device.DeviceKind}");
```

The Windows decoder supplies `VolumeName` as a drive letter, such as `E`, without
a colon or trailing backslash. `VolumeFlag` is a flags enum: `None = 0`,
`Media = 1`, and `Network = 2`. Test individual bits when a combination is
possible.

Consumers should inspect the payload before assuming it is a volume:

```csharp
using Brows.Sys;
using Brows.Sys.Messages.DeviceMessages;
using Brows.Sys.Messages.DeviceMessages.Devices;
using System;

static void PrintMessage(ISystemMessage message) {
    if (message is DeviceChange { Device: VolumeDevice volume } change) {
        Console.WriteLine($"{change.DeviceChangeKind}: {volume.VolumeName} ({volume.VolumeFlag})");
    }
}
```

The supplied decoder can emit a recognized device-change event with a null
`Device` when its native device type has no corresponding model; payloads whose
native device type is outside the platform's enumeration produce no message. The
device kind is available through `Device.DeviceKind`; there is no separate kind
property on `DeviceChange`.

## Clipboard notifications

`ClipboardChange` is emitted for each received clipboard-change notification.
No initial snapshot is sent. The message contains no clipboard contents, so
consumers should read any needed content separately. Its `SequenceNumber` is the
current clipboard sequence number for the calling window station when the
notification is processed. It is a state hint, not an event identifier or a
content snapshot. Zero means the value is unavailable, including when clipboard
access is limited. Delayed rendering can affect when the value advances. Queued
notifications can carry the same value, and the 32-bit value can wrap.
Consumers should accept zero and repeated values and should not treat the
sequence number as an unlimited monotonic counter.

A consumer can identify the message through the public `ISystemMessage`
contract:

```csharp
using Brows.Sys;
using Brows.Sys.Messages.ClipboardMessages;
using System;

static void HandleSystemMessage(ISystemMessage message) {
    if (message is ClipboardChange clipboardChange) {
        if (clipboardChange.SequenceNumber == 0) {
            Console.WriteLine("Clipboard changed; sequence number unavailable.");
            return;
        }

        Console.WriteLine($"Clipboard changed; sequence {clipboardChange.SequenceNumber}.");
    }
}
```

## Named devices and topology

```csharp
using Brows.Sys;
using Brows.Sys.Messages.DeviceMessages;
using Brows.Sys.Messages.DeviceMessages.Devices;
using System;

static void PrintDeviceNotification(ISystemMessage message) {
    switch (message) {
        case DeviceTreeChange:
            Console.WriteLine("Device inventory changed; re-enumerate devices.");
            break;
        case DeviceChange { Device: PortDevice port } change:
            Console.WriteLine($"{change.DeviceChangeKind}: {port.PortName}");
            break;
        case DeviceChange { Device: InterfaceDevice deviceInterface } change:
            Console.WriteLine($"{change.DeviceChangeKind}: {deviceInterface.InterfaceClassGuid}");
            Console.WriteLine(deviceInterface.InterfaceName);
            break;
    }
}
```

Interface names are copied native symbolic identifiers. Resolving friendly names,
device properties, and mounted paths belongs to the application. Port names are
the names supplied by the broadcast and are not restricted to COM-number syntax.

## Lifetime

When reading through `ISystemMessengerSet` from `Brows.Sys.Composition`, the
enumerator owns the internal messengers it creates and cleans them up when
reading ends. Use `await foreach`, or dispose an explicitly obtained async
enumerator, and cancel the reader when its window closes.

## License and source

[MIT license](https://licenses.nuget.org/MIT).
[Source repository](https://github.com/brows-app/brows-sys).
