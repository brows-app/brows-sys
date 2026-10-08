using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Brows.Sys;

[TestFixture]
internal sealed class ClipboardListenerRegistryTest {
    private static HwndSource CreateSource() {
        return new HwndSource(new HwndSourceParameters("ClipboardListenerRegistryTest") {
            Width = 1,
            Height = 1,
            WindowStyle = 0,
        });
    }

    private sealed class DispatcherThread : IDisposable {
        private readonly TaskCompletionSource<Dispatcher> DispatcherCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly Thread Thread;

        private void RunDispatcher() {
            DispatcherCompletion.TrySetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }

        internal Dispatcher Dispatcher { get; }

        internal int ThreadId => Dispatcher.Invoke(() => Environment.CurrentManagedThreadId);

        internal DispatcherThread() {
            Thread = new Thread(RunDispatcher);
            Thread.SetApartmentState(ApartmentState.STA);
            Thread.Start();
            Dispatcher = DispatcherCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }

        internal T Invoke<T>(Func<T> callback) {
            return Dispatcher.Invoke(
                callback,
                DispatcherPriority.Send,
                CancellationToken.None,
                TimeSpan.FromSeconds(10));
        }

        internal void Invoke(Action callback) {
            Dispatcher.Invoke(
                callback,
                DispatcherPriority.Send,
                CancellationToken.None,
                TimeSpan.FromSeconds(10));
        }

        internal void BeginShutdown() {
            Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        }

        public void Dispose() {
            if (!Dispatcher.HasShutdownStarted) {
                BeginShutdown();
            }
            var dispatcherThreadStopped = Thread.Join(TimeSpan.FromSeconds(10));
            if (!dispatcherThreadStopped) {
                throw new TimeoutException("The clipboard test dispatcher did not stop.");
            }
        }
    }

    private sealed class RecordingClipboardNativeApi : IClipboardNativeApi {
        private int Adds;
        private int Removes;
        private int AddErrorCode;
        private int RemoveErrorCode;
        private int SequenceNumber;
        private int AddThread;
        private int RemoveThread;

        internal Action<nint> AddAction { get; set; }

        internal int AddFailureCode {
            get => Volatile.Read(ref AddErrorCode);
            set => Volatile.Write(ref AddErrorCode, value);
        }

        internal int RemoveFailureCode {
            get => Volatile.Read(ref RemoveErrorCode);
            set => Volatile.Write(ref RemoveErrorCode, value);
        }

        internal int AddCount => Volatile.Read(ref Adds);

        internal int RemoveCount => Volatile.Read(ref Removes);

        internal int AddThreadId => Volatile.Read(ref AddThread);

        internal int RemoveThreadId => Volatile.Read(ref RemoveThread);

        internal void SetSequenceNumber(uint sequenceNumber) {
            Volatile.Write(ref SequenceNumber, unchecked((int)sequenceNumber));
        }

        public void AddClipboardFormatListener(nint hwnd) {
            Interlocked.Increment(ref Adds);
            Volatile.Write(ref AddThread, Environment.CurrentManagedThreadId);
            AddAction?.Invoke(hwnd);
            var errorCode = Volatile.Read(ref AddErrorCode);
            if (errorCode != 0) {
                throw new Win32Exception(errorCode, "Injected clipboard listener registration failure.");
            }
        }

        public void RemoveClipboardFormatListener(nint hwnd) {
            Interlocked.Increment(ref Removes);
            Volatile.Write(ref RemoveThread, Environment.CurrentManagedThreadId);
            var errorCode = Volatile.Read(ref RemoveErrorCode);
            if (errorCode != 0) {
                throw new Win32Exception(errorCode, "Injected clipboard listener removal failure.");
            }
        }

        public uint GetClipboardSequenceNumber() {
            return unchecked((uint)Volatile.Read(ref SequenceNumber));
        }

        public bool IsWindow(nint hwnd) {
            return NativeMethods.IsWindow(hwnd);
        }
    }

    private static class NativeMethods {
        [DllImport("user32.dll", EntryPoint = "IsWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(nint hwnd);
    }

    [Test]
    public void AcquireAndRelease_ReferenceCountsOneSourceAndIsIdempotent() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var api = new RecordingClipboardNativeApi();
        var registry = new ClipboardListenerRegistry(api);
        try {
            var first = dispatcher.Invoke(() => registry.Acquire(source));
            var second = dispatcher.Invoke(() => registry.Acquire(source));
            Assert.That(api.AddCount, Is.EqualTo(1));

            Task.Run(first.Dispose).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            Assert.That(api.RemoveCount, Is.Zero);

            second.Dispose();
            second.Dispose();
            Assert.That(api.RemoveCount, Is.EqualTo(1));
            Assert.That(dispatcher.Invoke(() => source.IsDisposed), Is.False);
        }
        finally {
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void Acquire_UsesIndependentRegistrationsForDifferentSources() {
        using var dispatcher = new DispatcherThread();
        var firstSource = dispatcher.Invoke(CreateSource);
        var secondSource = dispatcher.Invoke(CreateSource);
        var api = new RecordingClipboardNativeApi();
        var registry = new ClipboardListenerRegistry(api);
        try {
            var first = dispatcher.Invoke(() => registry.Acquire(firstSource));
            var second = dispatcher.Invoke(() => registry.Acquire(secondSource));
            Assert.That(api.AddCount, Is.EqualTo(2));

            first.Dispose();
            Assert.That(api.RemoveCount, Is.EqualTo(1));
            second.Dispose();
            Assert.That(api.RemoveCount, Is.EqualTo(2));
        }
        finally {
            dispatcher.Invoke(firstSource.Dispose);
            dispatcher.Invoke(secondSource.Dispose);
        }
    }

    [Test]
    public void AcquireAfterLastRelease_RegistersAgain() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var api = new RecordingClipboardNativeApi();
        var registry = new ClipboardListenerRegistry(api);
        try {
            var first = dispatcher.Invoke(() => registry.Acquire(source));
            first.Dispose();
            var second = dispatcher.Invoke(() => registry.Acquire(source));
            Assert.That(api.AddCount, Is.EqualTo(2));
            Assert.That(api.RemoveCount, Is.EqualTo(1));

            second.Dispose();
            Assert.That(api.RemoveCount, Is.EqualTo(2));
        }
        finally {
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void Acquire_WhenNativeAddFails_PreservesErrorAndLeavesNoRegistration() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var api = new RecordingClipboardNativeApi {
            AddFailureCode = 5,
        };
        var registry = new ClipboardListenerRegistry(api);
        try {
            var error = Assert.Throws<Win32Exception>(() => dispatcher.Invoke(() => registry.Acquire(source)));
            Assert.That(error.NativeErrorCode, Is.EqualTo(5));
            Assert.That(api.AddCount, Is.EqualTo(1));
            Assert.That(api.RemoveCount, Is.Zero);

            api.AddFailureCode = 0;
            var lease = dispatcher.Invoke(() => registry.Acquire(source));
            Assert.That(api.AddCount, Is.EqualTo(2));
            lease.Dispose();
            Assert.That(api.RemoveCount, Is.EqualTo(1));
        }
        finally {
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void Release_WhenNativeRemoveFailsOnLiveWindow_PreservesErrorAndCanBeRetriedByNextLease() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var api = new RecordingClipboardNativeApi();
        var registry = new ClipboardListenerRegistry(api);
        try {
            var first = dispatcher.Invoke(() => registry.Acquire(source));
            api.RemoveFailureCode = 5;
            var error = Assert.Throws<Win32Exception>(first.Dispose);
            Assert.That(error.NativeErrorCode, Is.EqualTo(5));
            Assert.That(api.RemoveCount, Is.EqualTo(1));

            api.RemoveFailureCode = 0;
            var second = dispatcher.Invoke(() => registry.Acquire(source));
            Assert.That(api.AddCount, Is.EqualTo(1));
            second.Dispose();
            Assert.That(api.RemoveCount, Is.EqualTo(2));
        }
        finally {
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void SourceClosure_InvalidatesOutstandingLeaseAndBlocksLaterDisposedHandlers() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var api = new RecordingClipboardNativeApi();
        var registry = new ClipboardListenerRegistry(api);
        var lease = dispatcher.Invoke(() => registry.Acquire(source));
        var reacquisitionError = default(Exception);
        source.Disposed += (_, _) => {
            try {
                registry.Acquire(source);
            }
            catch (Exception exception) {
                reacquisitionError = exception;
            }
        };

        dispatcher.Invoke(source.Dispose);

        Assert.That(reacquisitionError, Is.TypeOf<ObjectDisposedException>());
        Assert.That(api.AddCount, Is.EqualTo(1));
        Assert.That(api.RemoveCount, Is.EqualTo(1));
        Assert.DoesNotThrow(lease.Dispose);
    }


    [Test]
    public void LastLeaseReleaseDuringEarlierDisposedHandler_KeepsTombstoneForLaterHandlers() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var api = new RecordingClipboardNativeApi();
        var registry = new ClipboardListenerRegistry(api);
        IDisposable lease = null;
        var reacquisitionError = default(Exception);
        dispatcher.Invoke(() => source.Disposed += (_, _) => lease.Dispose());
        lease = dispatcher.Invoke(() => registry.Acquire(source));
        dispatcher.Invoke(() => source.Disposed += (_, _) => {
            try {
                registry.Acquire(source);
            }
            catch (Exception exception) {
                reacquisitionError = exception;
            }
        });

        dispatcher.Invoke(source.Dispose);

        Assert.That(reacquisitionError, Is.TypeOf<ObjectDisposedException>());
        Assert.That(api.AddCount, Is.EqualTo(1));
        Assert.That(api.RemoveCount, Is.EqualTo(1));
    }
    [Test]
    public void AcquireAndRelease_FromWorkerThreadUseSourceDispatcher() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var api = new RecordingClipboardNativeApi();
        var registry = new ClipboardListenerRegistry(api);
        try {
            var lease = Task.Run(() => registry.Acquire(source))
                .WaitAsync(TimeSpan.FromSeconds(10))
                .GetAwaiter()
                .GetResult();
            Assert.That(api.AddThreadId, Is.EqualTo(dispatcher.ThreadId));

            Task.Run(lease.Dispose).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            Assert.That(api.RemoveCount, Is.EqualTo(1));
            Assert.That(api.RemoveThreadId, Is.EqualTo(dispatcher.ThreadId));
        }
        finally {
            dispatcher.Invoke(source.Dispose);
        }
    }

    [Test]
    public void DispatcherShutdown_RacingWithLastLeaseReleaseRemovesOnce() {
        var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var api = new RecordingClipboardNativeApi();
        var registry = new ClipboardListenerRegistry(api);
        var lease = dispatcher.Invoke(() => registry.Acquire(source));

        var release = Task.Run(lease.Dispose);
        dispatcher.BeginShutdown();
        release.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        dispatcher.Dispose();

        Assert.That(api.RemoveCount, Is.EqualTo(1));
        Assert.DoesNotThrow(lease.Dispose);
    }

    [Test]
    public void GetSequenceNumber_ReturnsInjectedValues() {
        var api = new RecordingClipboardNativeApi();
        var registry = new ClipboardListenerRegistry(api);
        api.SetSequenceNumber(0);
        Assert.That(registry.GetSequenceNumber(), Is.Zero);
        api.SetSequenceNumber(uint.MaxValue);
        Assert.That(registry.GetSequenceNumber(), Is.EqualTo(uint.MaxValue));
    }

    [Test]
    public void Acquire_WhenAddClosesSourceDuringInitialization_DoesNotReturnLease() {
        using var dispatcher = new DispatcherThread();
        var source = dispatcher.Invoke(CreateSource);
        var api = new RecordingClipboardNativeApi();
        var registry = new ClipboardListenerRegistry(api);
        api.AddAction = _ => {
            source.Dispose();
        };

        var error = Assert.Throws<ObjectDisposedException>(() => dispatcher.Invoke(() => registry.Acquire(source)));

        Assert.That(error, Is.Not.Null);
        Assert.That(api.AddCount, Is.EqualTo(1));
        Assert.That(api.RemoveCount, Is.Zero);
    }
}
