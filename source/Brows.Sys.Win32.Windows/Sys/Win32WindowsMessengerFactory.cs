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
            Dispose();
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

        private void Cleanup(bool removeHook) {
            var errors = new List<Exception>();
            try {
                Source.Disposed -= OnOwnerClosing;
                Source.Dispatcher.ShutdownStarted -= OnOwnerClosing;
                var canRemoveHook = removeHook && !Source.IsDisposed;
                if (canRemoveHook) {
                    Source.RemoveHook(Hook);
                }
            }
            catch (Exception exception) {
                errors.Add(exception);
            }
            IDisposable[] owned;
            lock (Locker) {
                owned = Registrations.ToArray();
                Registrations.Clear();
            }
            foreach (var registration in owned) {
                try {
                    registration.Dispose();
                }
                catch (Exception exception) {
                    errors.Add(exception);
                }
            }
            if (errors.Count != 0) {
                throw new AggregateException("System listener cleanup failed.", errors);
            }
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
                    IDisposable clipboardRegistration;
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
            if (Interlocked.Exchange(ref Disposed, 1) != 0) {
                return;
            }
            SystemMessaged = null;
            var dispatcher = Source.Dispatcher;
            if (dispatcher.CheckAccess()) {
                Cleanup(true);
                return;
            }
            if (DispatcherIsShuttingDown(dispatcher)) {
                Cleanup(false);
                return;
            }
            try {
                dispatcher.Invoke(() => Cleanup(true));
            }
            catch (TaskCanceledException) when (DispatcherIsShuttingDown(dispatcher)) {
                Cleanup(false);
            }
            catch (InvalidOperationException) when (DispatcherIsShuttingDown(dispatcher)) {
                Cleanup(false);
            }
        }
    }
}
