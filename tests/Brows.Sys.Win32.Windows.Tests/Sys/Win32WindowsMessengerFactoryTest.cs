using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Brows.Sys;

[TestFixture]
internal sealed class Win32WindowsMessengerFactoryTest {
    private const uint DeviceChangeMessage = 0x0219;
    private const uint DeviceArrivalCode = 0x8000;

    private static HwndSource CreateSource() {
        return new HwndSource(new HwndSourceParameters("Win32WindowsMessengerFactoryTest") {
            Width = 1,
            Height = 1,
            WindowStyle = 0,
        });
    }

    private static Window CreateWindow() {
        return new Window {
            Width = 10,
            Height = 10,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
    }

    private static ISystemMessenger CreateMessenger(Win32WindowsMessengerFactory factory, nint hwnd) {
        return ((ISystemMessengerFactory)factory)
            .CreateSystemMessenger(hwnd, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10))
            .GetAwaiter()
            .GetResult();
    }

    private static void SendVolumeArrival(nint hwnd, nint payload) {
        NativeMethods.SendMessageW(hwnd, DeviceChangeMessage, (nint)DeviceArrivalCode, payload);
    }

    private sealed class DispatcherThread : IDisposable {
        private readonly TaskCompletionSource<Dispatcher> DispatcherCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly Thread Thread;
        private readonly Dispatcher Dispatcher;

        private void RunDispatcher() {
            DispatcherCompletion.TrySetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }

        public DispatcherThread() {
            Thread = new Thread(RunDispatcher);
            Thread.SetApartmentState(ApartmentState.STA);
            Thread.Start();
            Dispatcher = DispatcherCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }

        public T Invoke<T>(Func<T> callback) {
            return Dispatcher.Invoke(
                callback,
                DispatcherPriority.Send,
                CancellationToken.None,
                TimeSpan.FromSeconds(10));
        }

        public void Invoke(Action callback) {
            Dispatcher.Invoke(
                callback,
                DispatcherPriority.Send,
                CancellationToken.None,
                TimeSpan.FromSeconds(10));
        }

        public void Dispose() {
            Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            var dispatcherThreadStopped = Thread.Join(TimeSpan.FromSeconds(10));
            if (!dispatcherThreadStopped) {
                throw new TimeoutException("The WPF test dispatcher did not stop.");
            }
        }
    }

    private sealed class VolumeBroadcastPayload : IDisposable {
        private const int ByteLength = 20;

        public nint Pointer { get; } = Marshal.AllocHGlobal(ByteLength);

        public VolumeBroadcastPayload() {
            Marshal.WriteInt32(Pointer, 0, ByteLength);
            Marshal.WriteInt32(Pointer, 4, 2);
            Marshal.WriteInt32(Pointer, 8, 0);
            Marshal.WriteInt32(Pointer, 12, 1 << 2);
            Marshal.WriteInt16(Pointer, 16, 1);
        }

        public void Dispose() {
            Marshal.FreeHGlobal(Pointer);
        }
    }

    private static class NativeMethods {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
        internal static extern nint CreateWindowExW(
            uint extendedStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            nint parent,
            nint menu,
            nint instance,
            nint parameter);

        [DllImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(nint hwnd);

        [DllImport("user32.dll", EntryPoint = "IsWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(nint hwnd);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        internal static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    }

    private sealed class RecordingRegistrar : IDeviceNotificationRegistrar {
        private readonly int FailAt;
        private readonly bool ThrowOnRelease;
        private readonly Action Registered;
        private int Releases;

        internal List<Guid> Classes { get; } = [];
        internal int ReleaseCount => Volatile.Read(ref Releases);

        internal RecordingRegistrar(int failAt = 0, bool throwOnRelease = false, Action registered = null) {
            FailAt = failAt;
            ThrowOnRelease = throwOnRelease;
            Registered = registered;
        }

        public IDisposable Register(nint windowHandle, Guid interfaceClassGuid) {
            Classes.Add(interfaceClassGuid);
            if (Classes.Count == FailAt) {
                throw new Win32Exception(5, "Registration failed.");
            }
            Registered?.Invoke();
            return new Registration(this);
        }

        private sealed class Registration : IDisposable {
            private readonly RecordingRegistrar Owner;
            private int Released;
            internal Registration(RecordingRegistrar owner) {
                Owner = owner;
            }
            public void Dispose() {
                if (Interlocked.Exchange(ref Released, 1) != 0) {
                    return;
                }
                Interlocked.Increment(ref Owner.Releases);
                if (Owner.ThrowOnRelease) {
                    throw new InvalidOperationException("Release failed.");
                }
            }
        }
    }

    [Test]
    public void CreateSystemMessenger_WhenWindowHasNoWpfSource_ReturnsNull() {
        var hwnd = NativeMethods.CreateWindowExW(
            0,
            "STATIC",
            "Native test window",
            0,
            0,
            0,
            1,
            1,
            0,
            0,
            0,
            0);
        Assert.That(hwnd, Is.Not.EqualTo(0));

        try {
            Assert.That(HwndSource.FromHwnd(hwnd), Is.Null);

            var factory = new Win32WindowsMessengerFactory();
            var messenger = ((ISystemMessengerFactory)factory)
                .CreateSystemMessenger(hwnd, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10))
                .GetAwaiter()
                .GetResult();

            Assert.That(messenger, Is.Null);
        }
        finally {
            NativeMethods.DestroyWindow(hwnd);
        }
    }

    [Test]
    public void Dispose_RemovesHookWithoutDisposingSource_WhenCalledFromAnotherThread() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var factory = new Win32WindowsMessengerFactory();
        using var messenger = dispatcher.Invoke(() => CreateMessenger(factory, hwnd));
        Assert.That(messenger, Is.Not.Null);

        try {
            var messageCount = 0;
            messenger.SystemMessaged += (_, _) => Interlocked.Increment(ref messageCount);

            using var payload = new VolumeBroadcastPayload();
            dispatcher.Invoke(() => SendVolumeArrival(hwnd, payload.Pointer));
            Assert.That(Volatile.Read(ref messageCount), Is.EqualTo(1));

            Task.Run(messenger.Dispose).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            messenger.Dispose();

            dispatcher.Invoke(() => SendVolumeArrival(hwnd, payload.Pointer));
            Assert.That(Volatile.Read(ref messageCount), Is.EqualTo(1));

            var sourceState = dispatcher.Invoke(() => (source.IsDisposed, NativeMethods.IsWindow(hwnd)));
            Assert.That(sourceState.IsDisposed, Is.False);
            Assert.That(sourceState.Item2, Is.True);
        }
        finally {
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void CreateSystemMessenger_FromWorkerThread_AttachesHookOnOwnerDispatcher() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var factory = new Win32WindowsMessengerFactory();
        using var messenger = Task.Run(() => CreateMessenger(factory, hwnd))
            .WaitAsync(TimeSpan.FromSeconds(10))
            .GetAwaiter()
            .GetResult();
        Assert.That(messenger, Is.Not.Null);

        try {
            var messageCount = 0;
            messenger.SystemMessaged += (_, _) => Interlocked.Increment(ref messageCount);

            using var payload = new VolumeBroadcastPayload();
            dispatcher.Invoke(() => SendVolumeArrival(hwnd, payload.Pointer));
            Assert.That(Volatile.Read(ref messageCount), Is.EqualTo(1));

            Task.Run(messenger.Dispose).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            dispatcher.Invoke(() => SendVolumeArrival(hwnd, payload.Pointer));
            Assert.That(Volatile.Read(ref messageCount), Is.EqualTo(1));

            var sourceState = dispatcher.Invoke(() => (source.IsDisposed, NativeMethods.IsWindow(hwnd)));
            Assert.That(sourceState.IsDisposed, Is.False);
            Assert.That(sourceState.Item2, Is.True);
        }
        finally {
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void CreateSystemMessenger_FromWorkerThread_ResolvesWindowHandleOnOwnerDispatcher() {
        using var dispatcher = new DispatcherThread();
        var window = dispatcher.Invoke(() => {
            var created = CreateWindow();
            created.Show();
            return created;
        });
        var expectedHandle = dispatcher.Invoke(() => new WindowInteropHelper(window).Handle);
        Assert.That(expectedHandle, Is.Not.EqualTo(0));
        var factory = new Win32WindowsMessengerFactory();
        using var messenger = Task.Run(() => ((ISystemMessengerFactory)factory)
            .CreateSystemMessenger(window, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10))
            .GetAwaiter()
            .GetResult())
            .WaitAsync(TimeSpan.FromSeconds(10))
            .GetAwaiter()
            .GetResult();
        Assert.That(messenger, Is.Not.Null);

        try {
            var messageCount = 0;
            messenger.SystemMessaged += (_, _) => Interlocked.Increment(ref messageCount);

            using var payload = new VolumeBroadcastPayload();
            dispatcher.Invoke(() => SendVolumeArrival(expectedHandle, payload.Pointer));
            Assert.That(Volatile.Read(ref messageCount), Is.EqualTo(1));

            Task.Run(messenger.Dispose).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            dispatcher.Invoke(() => SendVolumeArrival(expectedHandle, payload.Pointer));
            Assert.That(Volatile.Read(ref messageCount), Is.EqualTo(1));

            var windowState = dispatcher.Invoke(() => (NativeMethods.IsWindow(expectedHandle), window.IsLoaded));
            Assert.That(windowState.Item1, Is.True);
            Assert.That(windowState.Item2, Is.True);
        }
        finally {
            dispatcher.Invoke(window.Close);
        }
    }

    [Test]
    public void Dispose_DoesNotThrow_WhenOwnerHasAlreadyClosedSource() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var factory = new Win32WindowsMessengerFactory();
        using var messenger = dispatcher.Invoke(() => CreateMessenger(factory, hwnd));
        Assert.That(messenger, Is.Not.Null);

        dispatcher.Invoke(source.Dispose);

        Assert.That(dispatcher.Invoke(() => source.IsDisposed), Is.True);
        Assert.That(NativeMethods.IsWindow(hwnd), Is.False);
        Assert.DoesNotThrow(messenger.Dispose);
        Assert.DoesNotThrow(messenger.Dispose);
    }

    [Test]
    public void RegistersExactExplorerProfileAndReleasesOnOwnerClosure() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var registrar = new RecordingRegistrar();
        using var messenger = CreateMessenger(new Win32WindowsMessengerFactory(registrar), hwnd);
        Assert.That(registrar.Classes, Is.EqualTo(new[] {
            new Guid("53F56307-B6BF-11D0-94F2-00A0C91EFB8B"),
            new Guid("53F5630D-B6BF-11D0-94F2-00A0C91EFB8B"),
            new Guid("6AC27878-A6FA-4155-BA85-F98F491D4F33"),
        }));
        dispatcher.Invoke(source.Dispose);
        Assert.That(registrar.ReleaseCount, Is.EqualTo(3));
        messenger.Dispose();
        Assert.That(registrar.ReleaseCount, Is.EqualTo(3));
    }

    [Test]
    public void RegistrationFailureRollsBackAllResourcesAndPreservesNativeError() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var hwnd = dispatcher.Invoke(() => source.Handle);
        try {
            var registrar = new RecordingRegistrar(failAt: 3, throwOnRelease: true);
            var error = Assert.Throws<Win32Exception>(() =>
                CreateMessenger(new Win32WindowsMessengerFactory(registrar), hwnd));
            Assert.That(error.NativeErrorCode, Is.EqualTo(5));
            Assert.That(registrar.ReleaseCount, Is.EqualTo(2));
            Assert.That(dispatcher.Invoke(() => source.IsDisposed), Is.False);
        }
        finally {
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void CancellationDuringRegistrationRollsBackWithoutDestroyingSource() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var hwnd = dispatcher.Invoke(() => source.Handle);
        using var cancellation = new CancellationTokenSource();
        try {
            var registrar = new RecordingRegistrar(registered: cancellation.Cancel);
            ISystemMessengerFactory factory = new Win32WindowsMessengerFactory(registrar);
            Assert.Catch<OperationCanceledException>(() =>
                factory.CreateSystemMessenger(hwnd, cancellation.Token).GetAwaiter().GetResult());
            Assert.That(registrar.ReleaseCount, Is.EqualTo(1));
            Assert.That(dispatcher.Invoke(() => source.IsDisposed), Is.False);
        }
        finally {
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void ExplicitDisposalAttemptsEveryReleaseAndIsIdempotent() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var registrar = new RecordingRegistrar(throwOnRelease: true);
        var messenger = CreateMessenger(new Win32WindowsMessengerFactory(registrar), hwnd);
        try {
            var error = Assert.Throws<AggregateException>(messenger.Dispose);
            Assert.That(error.InnerExceptions, Has.Count.EqualTo(3));
            Assert.That(registrar.ReleaseCount, Is.EqualTo(3));
            Assert.DoesNotThrow(messenger.Dispose);
            Assert.That(dispatcher.Invoke(() => source.IsDisposed), Is.False);
        }
        finally {
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void DispatcherShutdownReleasesLiveRegistrations() {
        var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var registrar = new RecordingRegistrar();
        using var messenger = CreateMessenger(new Win32WindowsMessengerFactory(registrar), hwnd);
        dispatcher.Dispose();
        Assert.That(registrar.ReleaseCount, Is.EqualTo(3));
        Assert.DoesNotThrow(messenger.Dispose);
    }

    [Test]
    public void OwnerClosureDuringInitializationStopsAndReleasesNewRegistration() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var registrar = new RecordingRegistrar(registered: source.Dispose);
        var messenger = CreateMessenger(new Win32WindowsMessengerFactory(registrar), hwnd);
        Assert.That(messenger, Is.Null);
        Assert.That(registrar.Classes, Has.Count.EqualTo(1));
        Assert.That(registrar.ReleaseCount, Is.EqualTo(1));
    }

    [Test]
    public async Task UnsupportedWindowAndPreCanceledCreationDoNotRegister() {
        var registrar = new RecordingRegistrar();
        ISystemMessengerFactory factory = new Win32WindowsMessengerFactory(registrar);
        Assert.That(factory.CreateSystemMessenger((nint)0, CancellationToken.None).GetAwaiter().GetResult(), Is.Null);
        await Assert.CatchAsync<OperationCanceledException>(async () => {
            await factory.CreateSystemMessenger((nint)0, new CancellationToken(true));
        });
        Assert.That(registrar.Classes, Is.Empty);
    }
}
