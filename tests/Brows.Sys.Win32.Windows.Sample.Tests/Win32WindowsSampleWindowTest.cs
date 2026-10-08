using Brows.Composition;
using Brows.Sys;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Interop;

namespace Brows;

[TestFixture]
[NonParallelizable]
internal sealed class Win32WindowsSampleWindowTest {
    private static void SendNamedDevice(nint handle, uint type, int offset, string name) {
        var bytes = Encoding.Unicode.GetBytes(name + "\0");
        var pointer = Marshal.AllocHGlobal(offset + bytes.Length);
        try {
            Marshal.WriteInt32(pointer, offset + bytes.Length);
            Marshal.WriteInt32(pointer, 4, (int)type);
            Marshal.WriteInt32(pointer, 8, 0);
            if (offset == 28) {
                var classGuid = new Guid("6AC27878-A6FA-4155-BA85-F98F491D4F33");
                Marshal.Copy(classGuid.ToByteArray(), 0, pointer + 12, 16);
            }
            Marshal.Copy(bytes, 0, pointer + offset, bytes.Length);
            NativeMethods.SendMessageW(handle, 0x0219, 0x8000, pointer);
        }
        finally {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [Test]
    public async Task ListedCompositionCapturesDeviceAndPostedClipboardEventsAndClosesAfterReaderCleanup() {
        using var dispatcher = new DispatcherThread();
        var imported = await dispatcher.Invoke(() => Imports.Init(new Environment(), default))
            .WaitAsync(TimeSpan.FromSeconds(10));
        var window = dispatcher.Invoke(() => imported.Find<Win32WindowsSampleWindow>());
        Assert.That(window, Is.Not.Null);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clipboardCaptured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clipboardRowsBeforePost = -1;
        HwndSource source = null;
        try {
            dispatcher.Invoke(() => {
                window.Closed += (_, _) => closed.TrySetResult();
                var log = (EventLog)window.DataContext;
                log.PropertyChanged += (_, e) => {
                    var deviceRowsCaptured = e.PropertyName == nameof(EventLog.Count) &&
                        log.Entries.Count(entry => entry.Category == SystemMessageKind.Device) == 5;
                    var clipboardRowCount = log.Entries.Count(entry => entry.Category == SystemMessageKind.Clipboard);
                    var clipboardRowsCaptured = clipboardRowsBeforePost >= 0 &&
                        clipboardRowCount > clipboardRowsBeforePost;
                    if (deviceRowsCaptured) {
                        captured.TrySetResult();
                    }
                    if (clipboardRowsCaptured) {
                        clipboardCaptured.TrySetResult();
                    }
                };
                var handle = new WindowInteropHelper(window).EnsureHandle();
                source = HwndSource.FromHwnd(handle);
                var payload = Marshal.AllocHGlobal(20);
                try {
                    Marshal.WriteInt32(payload, 0, 20);
                    Marshal.WriteInt32(payload, 4, 2);
                    Marshal.WriteInt32(payload, 8, 0);
                    Marshal.WriteInt32(payload, 12, (1 << 2) | (1 << 4));
                    Marshal.WriteInt16(payload, 16, 3);
                    Marshal.WriteInt16(payload, 18, 0);
                    NativeMethods.SendMessageW(handle, 0x0219, 0x8000, payload);
                }
                finally {
                    Marshal.FreeHGlobal(payload);
                }
                NativeMethods.SendMessageW(handle, 0x0219, 7, 0);
                SendNamedDevice(handle, 3, 12, "COM3");
                SendNamedDevice(handle, 5, 28, @"\\?\portable#device");
                clipboardRowsBeforePost = log.Entries.Count(entry => entry.Category == SystemMessageKind.Clipboard);
                Assert.That(NativeMethods.PostMessageW(handle, 0x031D, 0, 0), Is.True);
            });
            await captured.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await clipboardCaptured.Task.WaitAsync(TimeSpan.FromSeconds(10));
            dispatcher.Invoke(() => {
                var log = (EventLog)window.DataContext;
                Assert.That(log.Status, Is.EqualTo("Listening"));
                var deviceEntries = log.Entries.Where(entry => entry.Category == SystemMessageKind.Device).ToArray();
                Assert.That(deviceEntries.Select(entry => entry.DeviceName),
                    Is.EqualTo(new[] { "C", "E", "—", "COM3", @"\\?\portable#device" }));
                Assert.That(deviceEntries.Take(2).All(entry => entry.Flags == "Media, Network"), Is.True);
                Assert.That(deviceEntries[2].ChangeKind, Is.EqualTo("TreeChange"));
                Assert.That(deviceEntries[4].InterfaceClass, Is.EqualTo("6ac27878-a6fa-4155-ba85-f98f491d4f33"));
                var clipboardEntry = log.Entries
                    .Last(entry => entry.Category == SystemMessageKind.Clipboard);
                var sequenceIsDisplayed = clipboardEntry.ClipboardSequence == "Unavailable" ||
                    uint.TryParse(clipboardEntry.ClipboardSequence, NumberStyles.None,
                        CultureInfo.InvariantCulture, out _);
                Assert.That(sequenceIsDisplayed, Is.True);
                Assert.That(clipboardEntry.DeviceKind, Is.EqualTo("—"));
                Assert.That(clipboardEntry.DeviceName, Is.EqualTo("—"));
                Assert.That(clipboardEntry.InterfaceClass, Is.EqualTo("—"));
                Assert.That(clipboardEntry.Flags, Is.EqualTo("—"));
                window.Close();
            });
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            dispatcher.Invoke(() => {
                Assert.That(((EventLog)window.DataContext).Status, Is.EqualTo("Stopped"));
                Assert.That(source.IsDisposed, Is.True);
            });
        }
        finally {
            if (!closed.Task.IsCompleted) {
                dispatcher.Invoke(window.Close);
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            dispatcher.Invoke(() => {
                imported.Kill();
            });
        }
    }

    private sealed class Environment : IImportEnvironment {
        ImportInfo IImportEnvironment.ImportInfo => Win32WindowsSampleApp.CreateImportInfo();
    }

    private static class NativeMethods {
        [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        internal static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    }
}
