# Brows.Sys.Win32.Windows.Sample

A WPF viewer for the typed device notifications provided by `Brows.Sys`.
The application targets `net10.0-windows` and is not packable.

## Run

On Windows, use the SDK selected by the repository's `global.json`:

```powershell
dotnet run --project samples/Brows.Sys.Win32.Windows.Sample --configuration Release
```

Connect or disconnect removable storage, ports, or portable devices to generate notifications. The current
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

The grid shows local time, concrete message type, change kind, device kind,
device name, interface class GUID, and volume flags. No raw window-message inspector, export, filtering,
or persisted history is included.

## Composition and lifetime

The existing `ImportInfo.Listed` bootstrap creates the messenger set, Windows
factory, and sample window. Composition injects `ISystemMessengerSet` into the
window before it is shown modally. Reading starts after `SourceInitialized`;
row updates are marshaled to the window dispatcher.

The listener automatically registers disk, volume, and WPD interface classes.
Registration failure is surfaced through the existing error/status display;
partial registrations are rolled back. Source closure or dispatcher shutdown
also releases registrations.

Closing cancels capture and asynchronously waits for enumeration cleanup while
the dispatcher remains active. The modal dialog closes afterward, and the
application releases the import container. Startup failures produce an error
dialog and a nonzero exit code; stream failures remain visible in the viewer.
