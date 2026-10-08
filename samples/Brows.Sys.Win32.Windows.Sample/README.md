# Brows.Sys.Win32.Windows.Sample

A WPF viewer for the typed device and clipboard notifications provided by `Brows.Sys`.
The application targets `net10.0-windows` and is not packable.

## Run

On Windows, use the SDK selected by the repository's `global.json`:

```powershell
dotnet run --project samples/Brows.Sys.Win32.Windows.Sample --configuration Release
```

Connect or disconnect removable storage, ports, or portable devices to generate
device notifications. To generate clipboard notifications, copy text or files in
another application. The sample displays the category and clipboard sequence, but
never reads or previews clipboard contents. The current
library reports arrival, removal request, removal failure/cancellation, pending
removal, and completed removal. A native event affecting several drive letters
appears as one row per drive. Some supported device types have no modeled device
payload, displayed as `—`. Device-tree changes appear as `DeviceTreeChange`
with change category `TreeChange`. Port rows show the supplied port name;
interface rows show the native interface name and class GUID. Interface names
are identifiers, not resolved friendly names or mounted paths.

## Controls

- **Clear** removes displayed history without restarting the listener.
- **Pause** keeps draining the listener but omits newly received notifications.
  **Resume** captures subsequent notifications without replaying skipped events.
- **Auto-scroll** follows the newest event and is enabled by default.
- History is chronological and limited to the latest 1,000 rows. The status bar
  shows capture state, retained row count, and any listener error.

The grid shows local time, notification category, message type, change kind, clipboard
sequence, device kind, device name, interface class GUID, and volume flags. Clipboard
sequence zero is shown as Unavailable; device rows use an em dash in that column.
There is no raw message inspector, content preview, export, filtering, or persisted history.

## Composition and lifetime

The existing `ImportInfo.Listed` bootstrap creates the messenger set, Windows
factory, and sample window. Composition injects `ISystemMessengerSet` into the
window before it is shown modally. Reading starts after `SourceInitialized`;
row updates are marshaled to the window dispatcher.

The sample assembly has friend access to the libraries' internal types through
`InternalsVisibleTo`, allowing this explicit bootstrap. Application code outside
this repository should configure composition to discover the library assemblies
and resolve the public `ISystemMessengerSet`, as shown in the
[Windows adapter README](../../source/Brows.Sys.Win32.Windows/README.md).

The listener automatically registers disk, volume, and WPD interface classes and
the clipboard listener. Clipboard registration is shared by all active readers
for the same WPF source; ending one reader preserves the listener for others, and
the last reader releases it. Registration failure is surfaced through the existing
error/status display and rolls back partial initialization. Cancellation, source
closure, and dispatcher shutdown release owned registrations without disposing
the borrowed WPF source.

If clipboard listener registration fails while a window reader starts, composition
fails the stream with the native error. Sequence numbers are sampled when clipboard
notifications are processed. Zero means unavailable; values can repeat, delayed rendering can change
when they advance, and the 32-bit count can wrap. They are state hints rather than
event identifiers or snapshots of clipboard contents.

Closing cancels capture and asynchronously waits for enumeration cleanup while
the dispatcher remains active. The modal dialog closes afterward, and the
application releases the import container. Startup failures produce an error
dialog and a nonzero exit code; stream failures remain visible in the viewer.
