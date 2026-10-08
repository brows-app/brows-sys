# Brows.Sys.Composition

Composition contracts and an async stream that combines messages from system
messenger factories. This package targets `net10.0` and references `Brows.Sys`
and `Brows.Composition`.

## Installation

```powershell
dotnet add package Brows.Sys.Composition
```

A platform adapter supplies the actual notifications. For WPF on Windows, add
`Brows.Sys.Win32.Windows` instead; it includes this package transitively.

## Public contracts

`ISystemMessengerSet` is in `Brows.Sys` and inherits `Brows.Composition.IExport`.

- `ISystemMessengerSet.ReadAllSystemMessages(object window,
  CancellationToken cancellationToken)` returns an
  `IAsyncEnumerable<ISystemMessage>` that combines factory outputs.

`ISystemMessengerFactory` and `ISystemMessenger` are internal contracts.
Messenger implementations and creation are reserved for libraries in this
repository; applications consume the public message stream.

The default set implementation is internal and obtains its internal factories
through `Brows.Composition` imports. Initialize your application's composition
host with the `Brows.Sys.Composition` assembly and the supplied platform adapter
assembly (`Brows.Sys.Win32.Windows` for WPF). Once the host is ready, resolve the set:

```csharp
using Brows;
using Brows.Sys;

var messengerSet = Imports.Find<ISystemMessengerSet>(
    throwIfNotFound: true,
    throwIfNotReady: true);
```

Host initialization belongs to the application. See the
[Brows.Composition repository](https://github.com/brows-app/brows-composition)
for its import environment and initialization APIs.

## Read messages

Given a resolved set and a window accepted by one of its factories:

```csharp
using Brows.Sys;
using Brows.Sys.Messages.DeviceMessages;
using Brows.Sys.Messages.DeviceMessages.Devices;
using System;
using System.Threading;
using System.Threading.Tasks;

static async Task ReadDevicesAsync(
    ISystemMessengerSet messengerSet,
    object window,
    CancellationToken cancellationToken) {
    try {
        await foreach (var message in messengerSet.ReadAllSystemMessages(window, cancellationToken)) {
            if (message is DeviceChange { Device: VolumeDevice volume } change) {
                Console.WriteLine($"{change.DeviceChangeKind}: {volume.VolumeName}");
            }
        }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
    }
}
```

## Delivery and cleanup

- Enumeration starts factory creation. Each returned messenger is subscribed as
  it becomes available, so one pending factory does not delay other producers.
- Messages are buffered in an unbounded channel. Consume promptly; there is no
  bounded-buffer or backpressure setting.
- If all factories finish without producing a messenger, enumeration completes.
  An active reader otherwise waits for messages until it is canceled or disposed.
  The application should cancel it when its window closes.
- Ending enumeration detaches handlers, drains queued messages, and disposes
  each unique messenger. Successfully created late results are also disposed.
  Disposal is safe while a read is still outstanding: the abandoned read
  completes with a cancellation error, the same cleanup runs, and repeated
  disposal has no effect.
- Factory failures propagate to the reader. Cleanup failures on ordinary
  enumerator disposal are reported as an `AggregateException`; a factory or
  cancellation failure remains the primary exception. Errors from disposal of
  late results after enumeration ends are suppressed.

Use `await foreach`, or dispose an explicitly obtained async enumerator. Dispatch
updates to the appropriate UI thread when consuming from a background context.
The stream carries notifications and has no native-message return-value or veto
mechanism.

## License and source

[MIT license](https://licenses.nuget.org/MIT).
[Source repository](https://github.com/brows-app/brows-sys).
