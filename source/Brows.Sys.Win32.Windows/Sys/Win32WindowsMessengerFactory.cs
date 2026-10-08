using Brows.Sys.Messages;
using Brows.Win32;
using Brows.Win32.PlatformInvoke;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Brows.Sys;

internal sealed class Win32WindowsMessengerFactory : ISystemMessengerFactory {
    private readonly IDeviceNotificationRegistrar Registrar;
    private readonly ClipboardListenerRegistry ClipboardRegistry;

    private static bool DispatcherIsShuttingDown(Dispatcher dispatcher) {
        if (dispatcher is null) {
            throw new ArgumentNullException(nameof(dispatcher));
        }
        var isShuttingDown = dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished;
        return isShuttingDown;
    }

    private static nint ResolveWindowHandle(Window window, CancellationToken cancellationToken) {
        var dispatcher = window.Dispatcher;
        if (DispatcherIsShuttingDown(dispatcher)) {
            return 0;
        }
        if (dispatcher.CheckAccess()) {
            return new WindowInteropHelper(window).Handle;
        }
        try {
            return dispatcher.Invoke(() => new WindowInteropHelper(window).Handle,
                DispatcherPriority.Send, cancellationToken);
        }
        catch (TaskCanceledException) when (DispatcherIsShuttingDown(dispatcher)) {
            return 0;
        }
        catch (InvalidOperationException) when (DispatcherIsShuttingDown(dispatcher)) {
            return 0;
        }
    }

    internal Win32WindowsMessengerFactory(IDeviceNotificationRegistrar registrar)
        : this(registrar, ClipboardListenerRegistry.Shared) {
    }

    internal Win32WindowsMessengerFactory(IDeviceNotificationRegistrar registrar,
                                         ClipboardListenerRegistry clipboardRegistry) {
        Registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
        ClipboardRegistry = clipboardRegistry ?? throw new ArgumentNullException(nameof(clipboardRegistry));
    }

    public Win32WindowsMessengerFactory() : this(new Win32DeviceNotificationRegistrar()) {
    }

    Task<ISystemMessenger> ISystemMessengerFactory.CreateSystemMessenger(object window,
                                                                       CancellationToken cancellationToken) {
        if (cancellationToken.IsCancellationRequested) {
            return Task.FromCanceled<ISystemMessenger>(cancellationToken);
        }
        var hwnd =
            window is nint n ? n :
            window is Window w ? ResolveWindowHandle(w, cancellationToken) :
            0;
        cancellationToken.ThrowIfCancellationRequested();
        if (hwnd == 0) {
            return Task.FromResult<ISystemMessenger>(null);
        }
        var source = HwndSource.FromHwnd(hwnd);
        if (source is null) {
            return Task.FromResult<ISystemMessenger>(null);
        }
        var messenger = new Win32WindowsMessenger(hwnd, source, Registrar, ClipboardRegistry);
        if (!messenger.TryInitialize(cancellationToken)) {
            messenger.Dispose();
            return Task.FromResult<ISystemMessenger>(null);
        }
        return Task.FromResult<ISystemMessenger>(messenger);
    }

    private sealed class Win32WindowsMessenger : ISystemMessenger {
        private static readonly Guid[] InterfaceClasses = [
            new("53F56307-B6BF-11D0-94F2-00A0C91EFB8B"),
            new("53F5630D-B6BF-11D0-94F2-00A0C91EFB8B"),
            new("6AC27878-A6FA-4155-BA85-F98F491D4F33"),
        ];

        private readonly HwndSource Source;
        private readonly IDeviceNotificationRegistrar Registrar;
        private readonly ClipboardListenerRegistry ClipboardRegistry;
        private readonly Lock Locker = new();
        private readonly List<IDisposable> Registrations = [];
        private readonly List<Exception> DeferredOwnerCleanupErrors = [];
        private readonly TaskCompletionSource OwnerCleanupCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool OwnerCleanupInProgress;
        private int Disposed;

        private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) {
            var shouldPublish = Hwnd == hwnd && Volatile.Read(ref Disposed) == 0;
            if (shouldPublish) {
                var isClipboardUpdate = msg == (int)WM.CLIPBOARDUPDATE;
                if (isClipboardUpdate) {
                    var clipboardChange = new ClipboardChange {
                        SequenceNumber = ClipboardRegistry.GetSequenceNumber(),
                    };
                    SystemMessaged?.Invoke(this, new SystemMessageEventArgs(clipboardChange));
                    handled = false;
                    return 0;
                }
                foreach (var message in Win32Message.Interpret(msg, wParam, lParam)) {
                    SystemMessaged?.Invoke(this, new SystemMessageEventArgs(message));
                }
            }
            handled = false;
            return 0;
        }

        private void OnOwnerClosing(object sender, EventArgs args) {
            var dispatcherIsClosing = sender is Dispatcher;
            DisposeForOwnerClosure(dispatcherIsClosing);
        }

        private bool OwnerLifetimeHasEnded(Dispatcher dispatcher) {
            var sourceIsDisposed = Source.IsDisposed;
            var messengerIsDisposed = Volatile.Read(ref Disposed) != 0;
            var dispatcherIsShuttingDown = DispatcherIsShuttingDown(dispatcher);
            return sourceIsDisposed || messengerIsDisposed || dispatcherIsShuttingDown;
        }

        private void AddRegistration(IDisposable registration) {
            if (registration is null) {
                throw new InvalidOperationException("The system listener returned no owned registration.");
            }
            lock (Locker) {
                if (Volatile.Read(ref Disposed) == 0) {
                    Registrations.Add(registration);
                    return;
                }
            }
            registration.Dispose();
        }

        private bool TryStartOwnerCleanup() {
            lock (Locker) {
                if (Volatile.Read(ref Disposed) != 0) {
                    return false;
                }
                OwnerCleanupInProgress = true;
                Volatile.Write(ref Disposed, 1);
                return true;
            }
        }

        private bool TryStartForegroundCleanup() {
            lock (Locker) {
                if (Volatile.Read(ref Disposed) != 0) {
                    return false;
                }
                Volatile.Write(ref Disposed, 1);
                return true;
            }
        }

        private void WaitForOwnerCleanup() {
            var currentThreadCanRunOwnerCleanup = Source.Dispatcher.CheckAccess();
            bool ownerCleanupIsInProgress;
            lock (Locker) {
                ownerCleanupIsInProgress = OwnerCleanupInProgress;
            }
            var currentThreadWouldDeadlock = ownerCleanupIsInProgress && currentThreadCanRunOwnerCleanup;
            var shouldWaitForOwnerCleanup = ownerCleanupIsInProgress && !currentThreadWouldDeadlock;
            if (shouldWaitForOwnerCleanup) {
                OwnerCleanupCompleted.Task.GetAwaiter().GetResult();
            }
        }

        private List<Exception> Cleanup(bool removeHook,
                                        bool ownerIsClosing,
                                        bool dispatcherIsClosing) {
            var errors = new List<Exception>();
            try {
                Source.Disposed -= OnOwnerClosing;
            }
            catch (Exception exception) {
                errors.Add(exception);
            }
            try {
                Source.Dispatcher.ShutdownStarted -= OnOwnerClosing;
            }
            catch (Exception exception) {
                errors.Add(exception);
            }
            var canRemoveHook = removeHook && !Source.IsDisposed;
            if (canRemoveHook) {
                try {
                    Source.RemoveHook(Hook);
                }
                catch (Exception exception) {
                    errors.Add(exception);
                }
            }
            IDisposable[] owned;
            lock (Locker) {
                owned = Registrations.ToArray();
                Registrations.Clear();
            }
            foreach (var registration in owned) {
                try {
                    var isOwnerCleanupClipboardLease = ownerIsClosing &&
                        registration is ClipboardListenerRegistry.IClipboardListenerLease;
                    if (isOwnerCleanupClipboardLease) {
                        var lease = (ClipboardListenerRegistry.IClipboardListenerLease)registration;
                        var error = lease.DisposeForOwnerClosure(dispatcherIsClosing);
                        if (error is not null) {
                            errors.Add(error);
                        }
                    }
                    else {
                        registration.Dispose();
                    }
                }
                catch (Exception exception) {
                    errors.Add(exception);
                }
            }
            return errors;
        }

        private void DisposeForOwnerClosure(bool dispatcherIsClosing) {
            var ownerCleanupCanStart = TryStartOwnerCleanup();
            if (!ownerCleanupCanStart) {
                return;
            }
            try {
                SystemMessaged = null;
                var errors = Cleanup(removeHook: true, ownerIsClosing: true,
                    dispatcherIsClosing: dispatcherIsClosing);
                if (errors.Count != 0) {
                    lock (Locker) {
                        DeferredOwnerCleanupErrors.AddRange(errors);
                    }
                }
            }
            finally {
                lock (Locker) {
                    OwnerCleanupInProgress = false;
                }
                OwnerCleanupCompleted.TrySetResult();
            }
        }

        private void ThrowCleanupErrors(List<Exception> errors) {
            if (errors.Count != 0) {
                throw new AggregateException("System listener cleanup failed.", errors);
            }
        }

        private void ThrowDeferredOwnerCleanupErrors() {
            List<Exception> errors;
            lock (Locker) {
                if (DeferredOwnerCleanupErrors.Count == 0) {
                    return;
                }
                errors = [.. DeferredOwnerCleanupErrors];
                DeferredOwnerCleanupErrors.Clear();
            }
            ThrowCleanupErrors(errors);
        }

        internal Win32WindowsMessenger(nint hwnd,
                                       HwndSource source,
                                       IDeviceNotificationRegistrar registrar,
                                       ClipboardListenerRegistry clipboardRegistry) {
            Hwnd = hwnd;
            Source = source;
            Registrar = registrar;
            ClipboardRegistry = clipboardRegistry;
        }

        internal bool TryInitialize(CancellationToken cancellationToken) {
            var dispatcher = Source.Dispatcher;
            if (DispatcherIsShuttingDown(dispatcher)) {
                return false;
            }
            var initialized = false;
            void Initialize() {
                cancellationToken.ThrowIfCancellationRequested();
                var cannotInitialize = Source.IsDisposed || DispatcherIsShuttingDown(dispatcher);
                if (cannotInitialize) {
                    return;
                }
                try {
                    Source.Disposed += OnOwnerClosing;
                    dispatcher.ShutdownStarted += OnOwnerClosing;
                    Source.AddHook(Hook);
                    foreach (var interfaceClass in InterfaceClasses) {
                        cancellationToken.ThrowIfCancellationRequested();
                        var ownerHasClosed = Source.IsDisposed || Volatile.Read(ref Disposed) != 0 ||
                            DispatcherIsShuttingDown(dispatcher);
                        if (ownerHasClosed) {
                            return;
                        }
                        AddRegistration(Registrar.Register(Hwnd, interfaceClass));
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    var ownerLifetimeHasEnded = OwnerLifetimeHasEnded(dispatcher);
                    if (ownerLifetimeHasEnded) {
                        return;
                    }
                    ClipboardListenerRegistry.IClipboardListenerLease clipboardRegistration;
                    try {
                        clipboardRegistration = ClipboardRegistry.Acquire(Source);
                    }
                    catch (ObjectDisposedException) {
                        cancellationToken.ThrowIfCancellationRequested();
                        return;
                    }
                    AddRegistration(clipboardRegistration);
                    cancellationToken.ThrowIfCancellationRequested();
                    ownerLifetimeHasEnded = OwnerLifetimeHasEnded(dispatcher);
                    if (ownerLifetimeHasEnded) {
                        return;
                    }
                    initialized = Volatile.Read(ref Disposed) == 0;
                }
                catch {
                    /*
                     * Preserve the registration or cancellation error after attempting rollback.
                     */
                    try {
                        Dispose();
                    }
                    catch {
                    }
                    throw;
                }
            }
            try {
                if (dispatcher.CheckAccess()) {
                    Initialize();
                }
                else {
                    dispatcher.Invoke(Initialize, DispatcherPriority.Send, cancellationToken);
                }
            }
            catch (TaskCanceledException) when (DispatcherIsShuttingDown(dispatcher)) {
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }
            catch (InvalidOperationException) when (DispatcherIsShuttingDown(dispatcher)) {
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }
            return initialized;
        }

        public event SystemMessageEventHandler SystemMessaged;

        public nint Hwnd { get; }

        public void Dispose() {
            var foregroundCleanupCanStart = TryStartForegroundCleanup();
            if (!foregroundCleanupCanStart) {
                WaitForOwnerCleanup();
                ThrowDeferredOwnerCleanupErrors();
                return;
            }
            try {
                SystemMessaged = null;
                var dispatcher = Source.Dispatcher;
                if (dispatcher.CheckAccess()) {
                    ThrowCleanupErrors(Cleanup(removeHook: true, ownerIsClosing: false,
                        dispatcherIsClosing: false));
                    return;
                }
                if (DispatcherIsShuttingDown(dispatcher)) {
                    ThrowCleanupErrors(Cleanup(removeHook: false, ownerIsClosing: false,
                        dispatcherIsClosing: false));
                    return;
                }
                try {
                    dispatcher.Invoke(() => ThrowCleanupErrors(Cleanup(removeHook: true,
                        ownerIsClosing: false, dispatcherIsClosing: false)));
                }
                catch (TaskCanceledException) when (DispatcherIsShuttingDown(dispatcher)) {
                    ThrowCleanupErrors(Cleanup(removeHook: false, ownerIsClosing: false,
                        dispatcherIsClosing: false));
                }
                catch (InvalidOperationException) when (DispatcherIsShuttingDown(dispatcher)) {
                    ThrowCleanupErrors(Cleanup(removeHook: false, ownerIsClosing: false,
                        dispatcherIsClosing: false));
                }
            }
            finally {
                OwnerCleanupCompleted.TrySetResult();
            }
        }
    }
}
