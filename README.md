# Brows System

Typed operating-system notifications for the Brows file manager, a Windows File
Explorer replacement. The solution separates message contracts, composition,
Win32 decoding, and the WPF window adapter into four NuGet packages.

The current implementation reports device-tree changes, named ports, device interfaces,
device arrival/removal events, and clipboard updates. Volume notifications identify
every affected drive from A through Z and preserve media and network flags.
Clipboard notifications carry a sequence-number state hint without reading or copying
clipboard contents. File and folder change monitoring, Shell notifications, and
other window messages are not currently exposed.

## Packages

| Package | Target framework | Purpose |
| --- | --- | --- |
| [Brows.Sys](source/Brows.Sys/README.md) | `net10.0` | Contracts, device and clipboard changes, and volume models. |
| [Brows.Sys.Composition](source/Brows.Sys.Composition/README.md) | `net10.0` | Public async message stream and internal factories integrated with `Brows.Composition`. |
| [Brows.Sys.Win32](source/Brows.Sys.Win32/README.md) | `net10.0` | Windows-only native declarations and internal device-message decoding. |
| [Brows.Sys.Win32.Windows](source/Brows.Sys.Win32.Windows/README.md) | `net10.0-windows` | WPF window hooks that produce typed messages. |

For a WPF application, add the Windows adapter from your NuGet source:

```powershell
dotnet add package Brows.Sys.Win32.Windows
```

It brings in the other three packages through project/package dependencies.
Configure your `Brows.Composition` host to discover the Composition and Windows
adapter assemblies, then resolve `ISystemMessengerSet`. Start reading after the
window's native handle has been created and cancel the reader when the window
closes. See the [Windows adapter README](source/Brows.Sys.Win32.Windows/README.md)
for an example.

`Brows.Sys` and `Brows.Sys.Composition` contain framework-neutral contracts
and orchestration. The supplied Win32 backend and WPF adapter run on Windows.
Messenger creation is reserved for libraries in this repository:
`ISystemMessenger` and `ISystemMessengerFactory` are internal. Applications
resolve the public `ISystemMessengerSet` through composition and consume its
message stream; they cannot implement messengers or factories or create
messengers directly through the public API.

## Solution structure

- `source/` contains the four packable projects listed above.
- `tests/Brows.Sys.Tests/` is the core test project.
- `tests/Brows.Sys.Composition.Tests/` tests stream initialization, delivery,
  cancellation, and cleanup.
- `tests/Brows.Sys.Win32.Tests/` tests native payload decoding with synthetic
  buffers.
- `tests/Brows.Sys.Win32.Windows.Tests/` tests WPF hook and window lifetimes.
- [Brows.Sys.Win32.Windows.Sample](samples/Brows.Sys.Win32.Windows.Sample/README.md)
  displays live typed device and clipboard notifications in a WPF event log. Run
  it on Windows with `dotnet run --project samples/Brows.Sys.Win32.Windows.Sample --configuration Release`.
- `Directory.Build.props` supplies common framework, language, and assembly
  settings. `source/Directory.Build.props` and `tests/Directory.Build.props`
  extend those settings for their project groups.
- `Directory.Packages.props` manages dependency versions centrally.
- `.github/workflows/workflow.yml` builds and tests on Windows and packages
  releases.

## Build, test, and pack

Use Windows and the .NET SDK selected by `global.json`: .NET 10.0.100 with
feature-band roll forward. The code uses C# 14. Building the WPF adapter and its
tests requires the Windows Desktop targeting pack.

Run from the repository root:

```powershell
dotnet restore brows-sys.slnx
dotnet build brows-sys.slnx --no-restore --configuration Release
dotnet test brows-sys.slnx --no-build --no-restore --configuration Release
dotnet pack brows-sys.slnx --no-build --no-restore --configuration Release
```

The test projects are not packable. Generated binaries, test output, packages,
and symbol packages are placed under `out/` and should not be committed.
Only use `--no-build` after building the current code in the same configuration.

## Package documentation

Each source project has its own `README.md`. Shared source build properties set
`PackageReadmeFile` to `README.md` and pack that project's file at the root of its
NuGet package. The solution README remains repository documentation.

## License

[MIT](LICENSE). Copyright (c) 2026 Ken Yourek.
