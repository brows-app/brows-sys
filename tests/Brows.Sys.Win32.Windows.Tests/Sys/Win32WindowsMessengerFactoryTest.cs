using Brows.Sys.Messages;
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
    private const uint ClipboardUpdateMessage = 0x031D;
    private const uint DeviceChangeMessage = 0x0219;
    private const uint DeviceArrivalCode = 0x8000;

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

    private static Win32WindowsMessengerFactory CreateFactory(
        RecordingRegistrar registrar,
        RecordingClipboardNativeApi clipboardApi) {
        return new Win32WindowsMessengerFactory(registrar, new ClipboardListenerRegistry(clipboardApi));
    }

    private static void SendVolumeArrival(nint hwnd, nint payload) {
        NativeMethods.SendMessageW(hwnd, DeviceChangeMessage, (nint)DeviceArrivalCode, payload);
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

    private sealed class RecordingClipboardNativeApi : IClipboardNativeApi {
        private readonly Queue<uint> SequenceNumbers = [];
        private readonly int AddErrorCode;
        private readonly Action Added;
        private int AddCountValue;
        private int RemoveCountValue;
        private int SequenceReadCountValue;

        internal int AddCount => Volatile.Read(ref AddCountValue);

        internal int RemoveCount => Volatile.Read(ref RemoveCountValue);

        internal int SequenceReadCount => Volatile.Read(ref SequenceReadCountValue);

        internal bool ThrowOnRemove { get; set; }

        internal ManualResetEventSlim RemoveStarted { get; set; }

        internal ManualResetEventSlim ContinueRemove { get; set; }

        internal RecordingClipboardNativeApi(
            int addErrorCode = 0,
            bool throwOnRemove = false,
            Action added = null,
            params uint[] sequenceNumbers) {
            AddErrorCode = addErrorCode;
            ThrowOnRemove = throwOnRemove;
            Added = added;
            foreach (var sequenceNumber in sequenceNumbers ?? []) {
                SequenceNumbers.Enqueue(sequenceNumber);
            }
        }

        public void AddClipboardFormatListener(nint hwnd) {
            Interlocked.Increment(ref AddCountValue);
            if (AddErrorCode != 0) {
                throw new Win32Exception(AddErrorCode, "Clipboard registration failed.");
            }
            Added?.Invoke();
        }

        public void RemoveClipboardFormatListener(nint hwnd) {
            Interlocked.Increment(ref RemoveCountValue);
            RemoveStarted?.Set();
            var continueRemove = ContinueRemove;
            if (continueRemove is not null) {
                var removalCanContinue = continueRemove.Wait(TimeSpan.FromSeconds(10));
                if (!removalCanContinue) {
                    throw new TimeoutException("The injected clipboard removal did not continue.");
                }
            }
            if (ThrowOnRemove) {
                throw new Win32Exception(6, "Clipboard cleanup failed.");
            }
        }

        public uint GetClipboardSequenceNumber() {
            Interlocked.Increment(ref SequenceReadCountValue);
            if (SequenceNumbers.Count == 0) {
                return 0;
            }
            return SequenceNumbers.Dequeue();
        }

        public bool IsWindow(nint hwnd) {
            return true;
        }
    }

    [Test]
    public void ClipboardUpdateMessagePublishesEverySequenceValueWithoutReadingParameters() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var api = new RecordingClipboardNativeApi(sequenceNumbers: [0u, 17u, 17u, uint.MaxValue]);
        using var messenger = CreateMessenger(CreateFactory(new RecordingRegistrar(), api), hwnd);
        var messages = new List<ClipboardChange>();
        messenger.SystemMessaged += (_, args) => {
            if (args.Message is ClipboardChange change) {
                messages.Add(change);
            }
        };
        var observerCount = 0;
        var observerHandledValue = 1;
        nint ObserveClipboardUpdate(nint observedHwnd, int message, nint wParam, nint lParam,
                                    ref bool handled) {
            if (message == ClipboardUpdateMessage) {
                Interlocked.Increment(ref observerCount);
                Interlocked.Exchange(ref observerHandledValue, handled ? 1 : 0);
            }
            return 0;
        }

        dispatcher.Invoke(() => source.AddHook(ObserveClipboardUpdate));
        try {
            Assert.That(messages, Is.Empty);
            for (var index = 0; index < 4; index++) {
                var nativeResult = dispatcher.Invoke(() => NativeMethods.SendMessageW(
                    hwnd, ClipboardUpdateMessage, (nint)1, (nint)2));
                Assert.That(nativeResult, Is.EqualTo((nint)0));
            }

            Assert.That(messages, Has.Count.EqualTo(4));
            Assert.That(messages[0].SequenceNumber, Is.Zero);
            Assert.That(messages[1].SequenceNumber, Is.EqualTo(17u));
            Assert.That(messages[2].SequenceNumber, Is.EqualTo(17u));
            Assert.That(messages[3].SequenceNumber, Is.EqualTo(uint.MaxValue));
            Assert.That(api.SequenceReadCount, Is.EqualTo(4));
            Assert.That(Volatile.Read(ref observerCount), Is.EqualTo(4));
            Assert.That(Volatile.Read(ref observerHandledValue), Is.Zero);
        }
        finally {
            dispatcher.Invoke(() => source.RemoveHook(ObserveClipboardUpdate));
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void ProductionSharedDefaultFactory_RegistersAndPublishesClipboardUpdates() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        try {
            var hwnd = dispatcher.Invoke(() => source.Handle);
            using var messenger = CreateMessenger(new Win32WindowsMessengerFactory(), hwnd);
            Assert.That(messenger, Is.Not.Null);
            var clipboardMessageCount = 0L;
            messenger.SystemMessaged += (_, args) => {
                if (args.Message is ClipboardChange) {
                    Interlocked.Increment(ref clipboardMessageCount);
                }
            };

            var clipboardMessageCountBeforeSend = Interlocked.Read(ref clipboardMessageCount);
            dispatcher.Invoke(() => NativeMethods.SendMessageW(
                hwnd,
                ClipboardUpdateMessage,
                nint.Zero,
                nint.Zero));
            var clipboardMessageCountAfterSend = Interlocked.Read(ref clipboardMessageCount);

            Assert.That(
                clipboardMessageCountAfterSend,
                Is.GreaterThan(clipboardMessageCountBeforeSend));
        }
        finally {
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void FactoriesSharingSourceKeepClipboardRegistrationUntilLastMessengerIsDisposed() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var api = new RecordingClipboardNativeApi(sequenceNumbers: [11u, 12u, 13u]);
        var registry = new ClipboardListenerRegistry(api);
        var firstRegistrar = new RecordingRegistrar();
        var secondRegistrar = new RecordingRegistrar();
        var firstFactory = new Win32WindowsMessengerFactory(firstRegistrar, registry);
        var secondFactory = new Win32WindowsMessengerFactory(secondRegistrar, registry);
        var firstMessenger = CreateMessenger(firstFactory, hwnd);
        var secondMessenger = CreateMessenger(secondFactory, hwnd);
        var firstClipboardMessageCount = 0;
        var secondClipboardMessageCount = 0;
        firstMessenger.SystemMessaged += (_, args) => {
            if (args.Message is ClipboardChange) {
                Interlocked.Increment(ref firstClipboardMessageCount);
            }
        };
        secondMessenger.SystemMessaged += (_, args) => {
            if (args.Message is ClipboardChange) {
                Interlocked.Increment(ref secondClipboardMessageCount);
            }
        };

        try {
            Assert.That(api.AddCount, Is.EqualTo(1));
            dispatcher.Invoke(() => NativeMethods.SendMessageW(hwnd, ClipboardUpdateMessage, (nint)1, (nint)2));
            Assert.That(Volatile.Read(ref firstClipboardMessageCount), Is.EqualTo(1));
            Assert.That(Volatile.Read(ref secondClipboardMessageCount), Is.EqualTo(1));

            firstMessenger.Dispose();
            Assert.That(api.RemoveCount, Is.Zero);
            dispatcher.Invoke(() => NativeMethods.SendMessageW(hwnd, ClipboardUpdateMessage, (nint)1, (nint)2));
            Assert.That(Volatile.Read(ref firstClipboardMessageCount), Is.EqualTo(1));
            Assert.That(Volatile.Read(ref secondClipboardMessageCount), Is.EqualTo(2));

            secondMessenger.Dispose();
            Assert.That(api.RemoveCount, Is.EqualTo(1));
            dispatcher.Invoke(() => NativeMethods.SendMessageW(hwnd, ClipboardUpdateMessage, (nint)1, (nint)2));
            Assert.That(Volatile.Read(ref firstClipboardMessageCount), Is.EqualTo(1));
            Assert.That(Volatile.Read(ref secondClipboardMessageCount), Is.EqualTo(2));
        }
        finally {
            firstMessenger.Dispose();
            secondMessenger.Dispose();
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void ClipboardRegistrationFailureRollsBackAllDeviceRegistrationsAndPreservesNativeError() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var registrar = new RecordingRegistrar(throwOnRelease: true);
        var api = new RecordingClipboardNativeApi(addErrorCode: 1234);

        try {
            var error = Assert.Throws<Win32Exception>(() => CreateMessenger(CreateFactory(registrar, api), hwnd));
            Assert.That(error.NativeErrorCode, Is.EqualTo(1234));
            Assert.That(registrar.Classes, Has.Count.EqualTo(3));
            Assert.That(registrar.ReleaseCount, Is.EqualTo(3));
            Assert.That(api.AddCount, Is.EqualTo(1));
            Assert.That(api.RemoveCount, Is.Zero);
            Assert.That(dispatcher.Invoke(() => source.IsDisposed), Is.False);
        }
        finally {
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void CancellationAfterClipboardAcquisitionReleasesAllRegistrations() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        using var cancellation = new CancellationTokenSource();
        var registrar = new RecordingRegistrar();
        var api = new RecordingClipboardNativeApi(added: cancellation.Cancel);
        ISystemMessengerFactory factory = CreateFactory(registrar, api);

        try {
            Assert.Catch<OperationCanceledException>(() =>
                factory.CreateSystemMessenger(hwnd, cancellation.Token).GetAwaiter().GetResult());
            Assert.That(registrar.ReleaseCount, Is.EqualTo(3));
            Assert.That(api.AddCount, Is.EqualTo(1));
            Assert.That(api.RemoveCount, Is.EqualTo(1));
            Assert.That(dispatcher.Invoke(() => source.IsDisposed), Is.False);
        }
        finally {
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void SourceClosureDuringClipboardAcquisitionReturnsNullAndReleasesDeviceRegistrations() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var registrar = new RecordingRegistrar();
        var api = new RecordingClipboardNativeApi(added: source.Dispose);

        var messenger = CreateMessenger(CreateFactory(registrar, api), hwnd);

        Assert.That(messenger, Is.Null);
        Assert.That(registrar.Classes, Has.Count.EqualTo(3));
        Assert.That(registrar.ReleaseCount, Is.EqualTo(3));
        Assert.That(api.AddCount, Is.EqualTo(1));
        Assert.That(api.RemoveCount, Is.Zero);
        Assert.That(dispatcher.Invoke(() => source.IsDisposed), Is.True);
    }

    [Test]
    public void FactoryCreatedAfterRegistryOwnerClosureReturnsNullAndReleasesNewRegistrations() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var api = new RecordingClipboardNativeApi();
        var registry = new ClipboardListenerRegistry(api);
        using var existingMessenger = CreateMessenger(
            new Win32WindowsMessengerFactory(new RecordingRegistrar(), registry), hwnd);
        var laterRegistrar = new RecordingRegistrar();
        ISystemMessenger laterMessenger = null;
        Exception creationError = null;
        source.Disposed += (_, _) => {
            try {
                laterMessenger = CreateMessenger(
                    new Win32WindowsMessengerFactory(laterRegistrar, registry), hwnd);
            }
            catch (Exception exception) {
                creationError = exception;
            }
        };

        dispatcher.Invoke(source.Dispose);

        Assert.That(creationError, Is.Null);
        Assert.That(laterMessenger, Is.Null);
        Assert.That(laterRegistrar.Classes, Has.Count.EqualTo(3));
        Assert.That(laterRegistrar.ReleaseCount, Is.EqualTo(3));
        Assert.That(api.AddCount, Is.EqualTo(1));
    }

    [Test]
    public void CleanupAttemptsClipboardAndEveryDeviceReleaseWhenRemovalFails() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var registrar = new RecordingRegistrar(throwOnRelease: true);
        var api = new RecordingClipboardNativeApi(throwOnRemove: true);
        var messenger = CreateMessenger(CreateFactory(registrar, api), hwnd);

        try {
            var error = Assert.Throws<AggregateException>(messenger.Dispose);
            Assert.That(error.InnerExceptions, Has.Count.EqualTo(4));
            Assert.That(registrar.ReleaseCount, Is.EqualTo(3));
            Assert.That(api.RemoveCount, Is.EqualTo(1));
            Assert.DoesNotThrow(messenger.Dispose);
        }
        finally {
            api.ThrowOnRemove = false;
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void SourceClosure_WhenCleanupFails_StillNotifiesLaterSubscribersAndReportsOnce() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var registrar = new RecordingRegistrar(throwOnRelease: true);
        var api = new RecordingClipboardNativeApi(throwOnRemove: true);
        var messenger = CreateMessenger(CreateFactory(registrar, api), hwnd);
        var laterSubscriberRan = 0;
        dispatcher.Invoke(() => source.Disposed += (_, _) => Interlocked.Increment(ref laterSubscriberRan));

        Assert.DoesNotThrow(() => dispatcher.Invoke(source.Dispose));

        Assert.That(Volatile.Read(ref laterSubscriberRan), Is.EqualTo(1));
        Assert.That(registrar.ReleaseCount, Is.EqualTo(3));
        Assert.That(api.RemoveCount, Is.EqualTo(1));
        var error = Assert.Throws<AggregateException>(messenger.Dispose);
        Assert.That(error.Flatten().InnerExceptions, Has.Count.EqualTo(4));
        Assert.DoesNotThrow(messenger.Dispose);
        Assert.That(registrar.ReleaseCount, Is.EqualTo(3));
        Assert.That(api.RemoveCount, Is.EqualTo(1));
    }

    [Test]
    public void DispatcherShutdown_WhenCleanupFails_CompletesAndReportsOnce() {
        var dispatcher = new DispatcherThread();
        HwndSource source = null;
        ISystemMessenger messenger = null;
        Exception shutdownError = null;
        var laterShutdownSubscriberRan = 0;

        try {
            source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
            var hwnd = dispatcher.Invoke(() => source.Handle);
            var registrar = new RecordingRegistrar(throwOnRelease: true);
            var api = new RecordingClipboardNativeApi(throwOnRemove: true);
            messenger = CreateMessenger(CreateFactory(registrar, api), hwnd);
            dispatcher.Invoke(() => dispatcher.Dispatcher.ShutdownStarted += (_, _) => {
                try {
                    if (!source.IsDisposed) {
                        source.Dispose();
                    }
                }
                catch (Exception) {
                }
                Interlocked.Increment(ref laterShutdownSubscriberRan);
            });

            try {
                dispatcher.Invoke(() => dispatcher.Dispatcher.InvokeShutdown());
            }
            catch (Exception exception) {
                shutdownError = exception;
            }

            var shutdownFailed = shutdownError is not null;
            var dispatcherNeedsFailureFallback = shutdownFailed && dispatcher.IsAlive &&
                !dispatcher.Dispatcher.HasShutdownFinished;
            if (dispatcherNeedsFailureFallback) {
                try {
                    dispatcher.Invoke(() => {
                        if (!source.IsDisposed) {
                            try {
                                source.Dispose();
                            }
                            catch (Exception) {
                            }
                        }
                        Dispatcher.ExitAllFrames();
                    });
                }
                catch (Exception) {
                }
            }

            dispatcher.Dispose();
            Assert.That(shutdownError, Is.Null);
            Assert.That(laterShutdownSubscriberRan, Is.EqualTo(1));
            Assert.That(dispatcher.Dispatcher.HasShutdownFinished, Is.True);
            var error = Assert.Throws<AggregateException>(messenger.Dispose);
            Assert.That(error.Flatten().InnerExceptions, Has.Count.EqualTo(4));
            Assert.That(error.Flatten().InnerExceptions, Has.Some.TypeOf<Win32Exception>());
            Assert.That(registrar.ReleaseCount, Is.EqualTo(3));
            Assert.That(api.RemoveCount, Is.EqualTo(1));
            Assert.DoesNotThrow(messenger.Dispose);
        }
        finally {
            var dispatcherNeedsFailureFallback = dispatcher.IsAlive &&
                !dispatcher.Dispatcher.HasShutdownFinished;
            if (dispatcherNeedsFailureFallback && source is not null) {
                try {
                    dispatcher.Invoke(() => {
                        if (!source.IsDisposed) {
                            try {
                                source.Dispose();
                            }
                            catch (Exception) {
                            }
                        }
                        Dispatcher.ExitAllFrames();
                    });
                }
                catch (Exception) {
                }
            }
            dispatcher.Dispose();
        }
    }

    [Test]
    public async Task ForegroundDispose_WaitsForOwnerCleanupAndReportsItsFailureOnce() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        using var removeStarted = new ManualResetEventSlim();
        using var continueRemove = new ManualResetEventSlim();
        using var foregroundDisposeStarted = new ManualResetEventSlim();
        var registrar = new RecordingRegistrar();
        var api = new RecordingClipboardNativeApi(throwOnRemove: true) {
            RemoveStarted = removeStarted,
            ContinueRemove = continueRemove,
        };
        var messenger = CreateMessenger(CreateFactory(registrar, api), hwnd);
        var sourceClosure = Task.Run(() => dispatcher.Invoke(source.Dispose));
        var foregroundDisposalError = default(Exception);
        var foregroundDisposer = new Thread(() => {
            foregroundDisposeStarted.Set();
            try {
                messenger.Dispose();
            }
            catch (Exception exception) {
                foregroundDisposalError = exception;
            }
        });
        foregroundDisposer.IsBackground = true;

        try {
            Assert.That(removeStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);
            foregroundDisposer.Start();
            Assert.That(foregroundDisposeStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);
            var foregroundDisposalFinishedOrWaited = SpinWait.SpinUntil(
                () => !foregroundDisposer.IsAlive ||
                    (foregroundDisposer.ThreadState & ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(10));
            Assert.That(foregroundDisposalFinishedOrWaited, Is.True);
            Assert.That(foregroundDisposer.IsAlive, Is.True);

            continueRemove.Set();
            await sourceClosure.WaitAsync(TimeSpan.FromSeconds(10));
            var foregroundDisposalStopped = foregroundDisposer.Join(TimeSpan.FromSeconds(10));
            Assert.That(foregroundDisposalStopped, Is.True);
            var error = foregroundDisposalError as AggregateException;
            Assert.That(error, Is.Not.Null);
            Assert.That(error.Flatten().InnerExceptions, Has.Some.TypeOf<Win32Exception>());
            Assert.DoesNotThrow(messenger.Dispose);
            Assert.That(api.RemoveCount, Is.EqualTo(1));
            Assert.That(registrar.ReleaseCount, Is.EqualTo(3));
        }
        finally {
            continueRemove.Set();
            try {
                await sourceClosure.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception) {
            }
            if (foregroundDisposer.IsAlive) {
                var foregroundDisposalStopped = foregroundDisposer.Join(TimeSpan.FromSeconds(10));
                Assert.That(foregroundDisposalStopped, Is.True);
            }
            if (!dispatcher.Invoke(() => source.IsDisposed)) {
                dispatcher.Invoke(source.Dispose);
            }
        }
    }

    [Test]
    public void SharedOwnerClosureFailure_IsReportedOnceAndDoesNotRepeatListenerRemoval() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var api = new RecordingClipboardNativeApi(throwOnRemove: true);
        var registry = new ClipboardListenerRegistry(api);
        var firstRegistrar = new RecordingRegistrar();
        var secondRegistrar = new RecordingRegistrar();
        var first = CreateMessenger(new Win32WindowsMessengerFactory(firstRegistrar, registry), hwnd);
        var second = CreateMessenger(new Win32WindowsMessengerFactory(secondRegistrar, registry), hwnd);
        var laterSubscriberRan = 0;
        dispatcher.Invoke(() => source.Disposed += (_, _) => Interlocked.Increment(ref laterSubscriberRan));

        Assert.DoesNotThrow(() => dispatcher.Invoke(source.Dispose));

        Assert.That(Volatile.Read(ref laterSubscriberRan), Is.EqualTo(1));
        Assert.That(api.AddCount, Is.EqualTo(1));
        Assert.That(api.RemoveCount, Is.EqualTo(1));
        Assert.That(firstRegistrar.ReleaseCount, Is.EqualTo(3));
        Assert.That(secondRegistrar.ReleaseCount, Is.EqualTo(3));
        var error = Assert.Throws<AggregateException>(first.Dispose);
        Assert.That(error.Flatten().InnerExceptions, Has.Count.EqualTo(1));
        Assert.DoesNotThrow(second.Dispose);
        Assert.DoesNotThrow(first.Dispose);
        Assert.That(api.RemoveCount, Is.EqualTo(1));
    }

    [Test]
    public void ReleasedMessenger_DoesNotReceiveLaterSharedLeaseCleanupFailure() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var api = new RecordingClipboardNativeApi();
        var registry = new ClipboardListenerRegistry(api);
        var releasedRegistrar = new RecordingRegistrar();
        var activeRegistrar = new RecordingRegistrar();
        var releasedMessenger = CreateMessenger(
            new Win32WindowsMessengerFactory(releasedRegistrar, registry), hwnd);

        releasedMessenger.Dispose();
        Assert.That(api.RemoveCount, Is.EqualTo(1));
        var activeMessenger = CreateMessenger(
            new Win32WindowsMessengerFactory(activeRegistrar, registry), hwnd);
        Assert.That(api.AddCount, Is.EqualTo(2));
        api.ThrowOnRemove = true;

        dispatcher.Invoke(source.Dispose);

        Assert.That(api.RemoveCount, Is.EqualTo(2));
        Assert.DoesNotThrow(releasedMessenger.Dispose);
        Assert.Throws<AggregateException>(activeMessenger.Dispose);
        Assert.DoesNotThrow(activeMessenger.Dispose);
        Assert.That(releasedRegistrar.ReleaseCount, Is.EqualTo(3));
        Assert.That(activeRegistrar.ReleaseCount, Is.EqualTo(3));
    }

    [Test]
    public async Task OwnerCleanupFailure_IsReportedByPublicMessengerStreamDisposalOnce() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var registrar = new RecordingRegistrar();
        var api = new RecordingClipboardNativeApi(throwOnRemove: true);
        var factory = CreateFactory(registrar, api);
        var messengerSet = new SystemMessengerSet {
            Factories = [factory],
        };
        var enumerator = messengerSet.ReadSystemMessages(hwnd, CancellationToken.None).GetAsyncEnumerator();

        try {
            var moveNext = enumerator.MoveNextAsync().AsTask();
            dispatcher.Invoke(() => NativeMethods.SendMessageW(
                hwnd, ClipboardUpdateMessage, (nint)1, (nint)2));
            var hasMessage = await moveNext.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(hasMessage, Is.True);
            Assert.That(enumerator.Current, Is.TypeOf<ClipboardChange>());

            dispatcher.Invoke(source.Dispose);

            var error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await enumerator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.That(error.Flatten().InnerExceptions, Has.Some.TypeOf<Win32Exception>());
            await enumerator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(api.RemoveCount, Is.EqualTo(1));
            Assert.That(registrar.ReleaseCount, Is.EqualTo(3));
        }
        finally {
            api.ThrowOnRemove = false;
            try {
                await enumerator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception) {
            }
            if (!dispatcher.Invoke(() => source.IsDisposed)) {
                dispatcher.Invoke(source.Dispose);
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

            var factory = CreateFactory(new RecordingRegistrar(), new RecordingClipboardNativeApi());
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
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var factory = CreateFactory(new RecordingRegistrar(), new RecordingClipboardNativeApi());
        using var messenger = dispatcher.Invoke(() => CreateMessenger(factory, hwnd));
        Assert.That(messenger, Is.Not.Null);

        try {
            var messageCount = 0;
            messenger.SystemMessaged += (_, args) => {
                if (args.Message.SystemMessageKind == SystemMessageKind.Device) {
                    Interlocked.Increment(ref messageCount);
                }
            };

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
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var factory = CreateFactory(new RecordingRegistrar(), new RecordingClipboardNativeApi());
        using var messenger = Task.Run(() => CreateMessenger(factory, hwnd))
            .WaitAsync(TimeSpan.FromSeconds(10))
            .GetAwaiter()
            .GetResult();
        Assert.That(messenger, Is.Not.Null);

        try {
            var messageCount = 0;
            messenger.SystemMessaged += (_, args) => {
                if (args.Message.SystemMessageKind == SystemMessageKind.Device) {
                    Interlocked.Increment(ref messageCount);
                }
            };

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
        var factory = CreateFactory(new RecordingRegistrar(), new RecordingClipboardNativeApi());
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
            messenger.SystemMessaged += (_, args) => {
                if (args.Message.SystemMessageKind == SystemMessageKind.Device) {
                    Interlocked.Increment(ref messageCount);
                }
            };

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
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var registrar = new RecordingRegistrar();
        var api = new RecordingClipboardNativeApi();
        var factory = CreateFactory(registrar, api);
        using var messenger = dispatcher.Invoke(() => CreateMessenger(factory, hwnd));
        Assert.That(messenger, Is.Not.Null);
        Assert.That(api.AddCount, Is.EqualTo(1));

        dispatcher.Invoke(source.Dispose);

        Assert.That(dispatcher.Invoke(() => source.IsDisposed), Is.True);
        Assert.That(NativeMethods.IsWindow(hwnd), Is.False);
        Assert.That(registrar.ReleaseCount, Is.EqualTo(3));
        Assert.That(api.RemoveCount, Is.EqualTo(1));
        Assert.DoesNotThrow(messenger.Dispose);
        Assert.DoesNotThrow(messenger.Dispose);
    }

    [Test]
    public void RegistersExactExplorerProfileAndReleasesOnOwnerClosure() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var registrar = new RecordingRegistrar();
        using var messenger = CreateMessenger(CreateFactory(registrar, new RecordingClipboardNativeApi()), hwnd);
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
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        try {
            var registrar = new RecordingRegistrar(failAt: 3, throwOnRelease: true);
            var error = Assert.Throws<Win32Exception>(() =>
                CreateMessenger(CreateFactory(registrar, new RecordingClipboardNativeApi()), hwnd));
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
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        using var cancellation = new CancellationTokenSource();
        try {
            var registrar = new RecordingRegistrar(registered: cancellation.Cancel);
            ISystemMessengerFactory factory = CreateFactory(registrar, new RecordingClipboardNativeApi());
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
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var registrar = new RecordingRegistrar(throwOnRelease: true);
        var messenger = CreateMessenger(CreateFactory(registrar, new RecordingClipboardNativeApi()), hwnd);
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
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var registrar = new RecordingRegistrar();
        var api = new RecordingClipboardNativeApi();
        using var messenger = CreateMessenger(CreateFactory(registrar, api), hwnd);
        dispatcher.Dispose();
        Assert.That(registrar.ReleaseCount, Is.EqualTo(3));
        Assert.That(api.AddCount, Is.EqualTo(1));
        Assert.That(api.RemoveCount, Is.EqualTo(1));
        Assert.DoesNotThrow(messenger.Dispose);
    }

    [Test]
    public void OwnerClosureDuringInitializationStopsAndReleasesNewRegistration() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(() => DispatcherThread.CreateSource(nameof(Win32WindowsMessengerFactoryTest)));
        var hwnd = dispatcher.Invoke(() => source.Handle);
        var registrar = new RecordingRegistrar(registered: source.Dispose);
        var messenger = CreateMessenger(CreateFactory(registrar, new RecordingClipboardNativeApi()), hwnd);
        Assert.That(messenger, Is.Null);
        Assert.That(registrar.Classes, Has.Count.EqualTo(1));
        Assert.That(registrar.ReleaseCount, Is.EqualTo(1));
    }

    [Test]
    public async Task UnsupportedWindowAndPreCanceledCreationDoNotRegister() {
        var registrar = new RecordingRegistrar();
        ISystemMessengerFactory factory = CreateFactory(registrar, new RecordingClipboardNativeApi());
        Assert.That(factory.CreateSystemMessenger((nint)0, CancellationToken.None).GetAwaiter().GetResult(), Is.Null);
        await Assert.CatchAsync<OperationCanceledException>(async () => {
            await factory.CreateSystemMessenger((nint)0, new CancellationToken(true));
        });
        Assert.That(registrar.Classes, Is.Empty);
    }
}
