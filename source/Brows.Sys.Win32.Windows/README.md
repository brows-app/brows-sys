# Brows.Sys.Win32.Windows

A WPF adapter that turns Windows device-change messages into Brows system
notifications. The package targets `net10.0-windows` with WPF enabled and
references `Brows.Sys.Composition` and `Brows.Sys.Win32`.

## Installation

```powershell
dotnet add package Brows.Sys.Win32.Windows
```

Use a Windows application targeting `net10.0-windows`. A WPF host should have
`UseWpf` enabled. The package includes the core contracts, composition layer, and
native backend through its dependencies.

## Composition setup

The adapter's factory is internal and implements the internal
`ISystemMessengerFactory` contract. Configure your `Brows.Composition` host to
discover both the `Brows.Sys.Composition` and
`Brows.Sys.Win32.Windows` assemblies. Initialize the host, then obtain the
message set:

```csharp
using Brows;
using Brows.Sys;

var messengerSet = Imports.Find<ISystemMessengerSet>(
    throwIfNotFound: true,
    throwIfNotReady: true);
```

Messenger creation is reserved for libraries in this repository. Applications
use the public `ISystemMessengerSet`; `ISystemMessengerFactory`,
`ISystemMessenger`, and the adapter's implementation types are internal.
Composition supplies the implementation.

## Listen to a window

Start reading from the UI thread after the WPF window's `SourceInitialized`
event, when its native handle exists. Supply a cancellation token that is
canceled when the window closes.

```csharp
using Brows.Sys;
using Brows.Sys.Messages.DeviceMessages;
using Brows.Sys.Messages.DeviceMessages.Devices;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

static async Task WatchDevicesAsync(
    ISystemMessengerSet messengerSet,
    Window window,
    CancellationToken cancellationToken) {
    try {
        await foreach (var message in messengerSet.ReadAllSystemMessages(window, cancellationToken)) {
            if (message is DeviceChange { Device: VolumeDevice volume } change) {
                Console.WriteLine($"{change.DeviceChangeKind}: {volume.VolumeName} ({volume.VolumeFlag})");
            }
        }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
    }
}
```

This example receives an already initialized message set; initialize composition
once in the host rather than for every window. If consumption runs on a
background context, marshal UI changes to `window.Dispatcher`.

## Accepted windows and ownership

- The message stream accepts a WPF `Window` with an existing native handle, or a boxed
  `nint` whose handle belongs to a live `HwndSource` in the application. A
  `Window` resolves its handle on the window's dispatcher, so both forms work
  from a background thread; a window whose dispatcher is shutting down
  produces no messenger.
- Arbitrary native HWNDs without a WPF source are unsupported. A zero handle or
  a handle without a live WPF source produces no messenger. Hook attachment also
  declines a source that is disposed or whose dispatcher is shutting down.
- Hook attachment and removal run on the source's dispatcher. The composition
  stream disposes its internal messengers when enumeration ends.
- Disposal unregisters owned interface notifications and detaches this listener's
  hook. It preserves the borrowed window source and is safe to repeat after the
  owner closes the source.
- Closing the window does not itself complete the message stream. The host
  should cancel its reader and await the reading task's completion.

## Interface registration and cleanup

Every messenger automatically registers the disk, volume, and Windows Portable
Device interface classes using `RegisterDeviceNotificationW`. Registration is
performed on the source dispatcher. Port and volume broadcasts need no additional
registration. Basic broadcasts require a top-level receiver window.

Creation fails with a `Win32Exception` if a registration fails. Partial registrations
and hooks are rolled back while preserving the original error. Cancellation during
initialization uses the same rollback. The composition reader propagates these
failures, and the sample displays them in its existing error/status area.

Registrations are owned by safe handles and are released on listener disposal,
source closure, or dispatcher shutdown. Repeated cleanup is safe and never
disposes the borrowed WPF source. Window closure still does not complete the
composition stream; cancel the reader as before.

The profile is fixed. There is no subscription-configuration, device-enumeration,
metadata-enrichment, or removal-veto API.

## Notification scope

The adapter forwards device-tree notifications and the five supported arrival/removal
events decoded by `Brows.Sys.Win32`. Port and interface events include copied native
identities; device-tree events identify no individual device. A volume notification
affecting multiple drives produces
one managed message for each drive. Other recognized device types can have a
null `Device` payload.

The hook leaves Windows' normal message processing in place. Neither the async
stream nor its message records provide a way to approve or deny native queries,
including device-removal requests. File/folder changes, Shell notifications,
clipboard changes, and other window-message categories are not currently
published by this adapter.

## License and source

[MIT license](https://licenses.nuget.org/MIT).
[Source repository](https://github.com/brows-app/brows-sys).
