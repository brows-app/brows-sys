using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Brows.Sys;

internal sealed class ClipboardListenerRegistry {
    private const int WindowNonClientDestroyMessage = 0x0082;
    private static readonly TimeSpan DispatcherOperationTimeout = TimeSpan.FromSeconds(10);

    private readonly ConditionalWeakTable<HwndSource, ListenerState> States = new();
    private readonly Lock Locker = new();
    private readonly IClipboardNativeApi Api;

    private static bool DispatcherIsShuttingDown(Dispatcher dispatcher) {
        return dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished;
    }

    private ListenerState GetOrCreateState(HwndSource source, nint hwnd) {
        lock (Locker) {
            if (States.TryGetValue(source, out var existingState)) {
                return existingState;
            }

            var state = new ListenerState(this, Api, source, hwnd);
            States.Add(source, state);
            try {
                state.Attach(source);
            }
            catch {
                States.Remove(source);
                throw;
            }
            return state;
        }
    }

    private IClipboardListenerLease AcquireOnDispatcher(HwndSource source) {
        var dispatcher = source.Dispatcher;
        var sourceCannotBeAcquired = source.IsDisposed || DispatcherIsShuttingDown(dispatcher);
        if (sourceCannotBeAcquired) {
            throw new ObjectDisposedException(nameof(HwndSource));
        }

        var hwnd = source.Handle;
        if (hwnd == 0) {
            throw new ObjectDisposedException(nameof(HwndSource));
        }

        var state = GetOrCreateState(source, hwnd);
        return state.Acquire();
    }

    private void RemoveState(HwndSource source, ListenerState state) {
        if (source is null) {
            return;
        }
        lock (Locker) {
            var stateBelongsToSource = States.TryGetValue(source, out var current) &&
                ReferenceEquals(current, state);
            if (stateBelongsToSource) {
                States.Remove(source);
            }
        }
    }

    internal static ClipboardListenerRegistry Shared { get; } = new(new Win32ClipboardNativeApi());

    internal ClipboardListenerRegistry(IClipboardNativeApi api) {
        Api = api ?? throw new ArgumentNullException(nameof(api));
    }

    internal IClipboardListenerLease Acquire(HwndSource source) {
        if (source is null) {
            throw new ArgumentNullException(nameof(source));
        }

        var dispatcher = source.Dispatcher;
        if (dispatcher.CheckAccess()) {
            return AcquireOnDispatcher(source);
        }
        if (DispatcherIsShuttingDown(dispatcher)) {
            throw new ObjectDisposedException(nameof(HwndSource));
        }

        try {
            return dispatcher.Invoke(
                () => AcquireOnDispatcher(source),
                DispatcherPriority.Send,
                CancellationToken.None,
                DispatcherOperationTimeout);
        }
        catch (TaskCanceledException) when (DispatcherIsShuttingDown(dispatcher)) {
            throw new ObjectDisposedException(nameof(HwndSource));
        }
        catch (InvalidOperationException) when (DispatcherIsShuttingDown(dispatcher)) {
            throw new ObjectDisposedException(nameof(HwndSource));
        }
    }

    internal uint GetSequenceNumber() {
        return Api.GetClipboardSequenceNumber();
    }

    internal interface IClipboardListenerLease : IDisposable {
        Exception DisposeForOwnerClosure(bool dispatcherIsClosing);
    }

    private sealed class ListenerState {
        private readonly ClipboardListenerRegistry Registry;
        private readonly IClipboardNativeApi Api;
        private readonly WeakReference<HwndSource> Source;
        private readonly Dispatcher Dispatcher;
        private readonly nint Hwnd;
        private readonly Lock Locker = new();
        private int ReferenceCount;
        private bool IsRegistered;
        private bool IsAdding;
        private bool IsRemoving;
        private bool HasDisposedHandler;
        private bool HasShutdownHandler;
        private bool HasHook;
        private bool OwnerCleanupStarted;
        private readonly List<Exception> DeferredCleanupErrors = [];
        private OwnerState State;

        private bool SourceIsUnavailable(out HwndSource source) {
            var sourceIsReachable = Source.TryGetTarget(out source);
            return !sourceIsReachable || source.IsDisposed;
        }

        private bool RemoveListenerIfWindowIsLive() {
            if (!Api.IsWindow(Hwnd)) {
                return false;
            }

            try {
                Api.RemoveClipboardFormatListener(Hwnd);
                return true;
            }
            catch (Win32Exception) {
                if (Api.IsWindow(Hwnd)) {
                    throw;
                }
                return false;
            }
        }

        private void Detach(HwndSource source, bool removeHook) {
            var errors = new List<Exception>();
            var shouldDetachDisposedHandler = source is not null && HasDisposedHandler;
            if (shouldDetachDisposedHandler) {
                try {
                    source.Disposed -= OnSourceDisposed;
                    HasDisposedHandler = false;
                }
                catch (Exception exception) {
                    errors.Add(exception);
                }
            }
            if (HasShutdownHandler) {
                try {
                    Dispatcher.ShutdownStarted -= OnDispatcherShutdownStarted;
                    HasShutdownHandler = false;
                }
                catch (Exception exception) {
                    errors.Add(exception);
                }
            }
            var shouldDetachHook = source is not null && removeHook && HasHook;
            if (shouldDetachHook) {
                try {
                    source.RemoveHook(OnSourceHook);
                    HasHook = false;
                }
                catch (Exception exception) {
                    errors.Add(exception);
                }
            }
            if (errors.Count != 0) {
                throw new AggregateException("Clipboard listener owner cleanup failed.", errors);
            }
        }

        private Exception EndOwner(OwnerState finalState,
                                   bool removeRegisteredListener,
                                   bool removeStateImmediately,
                                   bool removeHook,
                                   bool scheduleStateRemoval) {
            bool shouldRemove;
            lock (Locker) {
                if (OwnerCleanupStarted) {
                    return null;
                }
                OwnerCleanupStarted = true;
                var ownerHasActiveRegistration = removeRegisteredListener && State == OwnerState.Active && IsRegistered;
                shouldRemove = ownerHasActiveRegistration;
                if (State == OwnerState.Active) {
                    State = finalState;
                }
                ReferenceCount = 0;
                IsRegistered = false;
            }

            var errors = new List<Exception>();
            if (shouldRemove) {
                try {
                    RemoveListenerIfWindowIsLive();
                }
                catch (Exception exception) {
                    errors.Add(exception);
                }
            }

            Source.TryGetTarget(out var source);
            var shouldRemoveHook = removeHook && source is not null && !source.IsDisposed;
            try {
                Detach(source, shouldRemoveHook);
            }
            catch (Exception exception) {
                errors.Add(exception);
            }

            try {
                if (source is not null) {
                    if (removeStateImmediately) {
                        Registry.RemoveState(source, this);
                    }
                    else if (scheduleStateRemoval) {
                        ScheduleStateRemoval();
                    }
                }
            }
            catch (Exception exception) {
                errors.Add(exception);
            }

            if (errors.Count == 0) {
                return null;
            }
            if (errors.Count == 1) {
                return errors[0];
            }
            return new AggregateException("Clipboard listener owner cleanup failed.", errors);
        }

        private void RecordDeferredCleanupError(Exception error) {
            if (error is null) {
                return;
            }
            lock (Locker) {
                DeferredCleanupErrors.Add(error);
            }
        }

        private Exception TakeDeferredCleanupError() {
            lock (Locker) {
                if (DeferredCleanupErrors.Count == 0) {
                    return null;
                }
                if (DeferredCleanupErrors.Count == 1) {
                    var error = DeferredCleanupErrors[0];
                    DeferredCleanupErrors.Clear();
                    return error;
                }
                var errors = DeferredCleanupErrors.ToArray();
                DeferredCleanupErrors.Clear();
                return new AggregateException("Clipboard listener owner cleanup failed.", errors);
            }
        }

        private void RemoveCompletedState() {
            if (!Source.TryGetTarget(out var source)) {
                return;
            }
            var dispatcherIsUnavailable = DispatcherIsShuttingDown(Dispatcher);
            var sourceAndDispatcherRemainActive = !source.IsDisposed && !dispatcherIsUnavailable;
            if (sourceAndDispatcherRemainActive) {
                return;
            }
            Registry.RemoveState(source, this);
        }

        private void ScheduleStateRemoval() {
            if (Dispatcher.HasShutdownFinished) {
                RemoveCompletedState();
                return;
            }
            try {
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(RemoveCompletedState));
            }
            catch (InvalidOperationException) when (DispatcherIsShuttingDown(Dispatcher)) {
                RemoveCompletedState();
            }
        }

        private void OnSourceDisposed(object sender, EventArgs args) {
            var error = EndOwner(OwnerState.WindowClosing, removeRegisteredListener: true,
                removeStateImmediately: false, removeHook: true, scheduleStateRemoval: true);
            RecordDeferredCleanupError(error);
        }

        private void OnDispatcherShutdownStarted(object sender, EventArgs args) {
            var error = EndOwner(OwnerState.DispatcherShuttingDown, removeRegisteredListener: true,
                removeStateImmediately: false, removeHook: true, scheduleStateRemoval: false);
            RecordDeferredCleanupError(error);
        }

        private nint OnSourceHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) {
            var messageDestroysThisWindow = hwnd == Hwnd && msg == WindowNonClientDestroyMessage;
            if (messageDestroysThisWindow) {
                lock (Locker) {
                    if (State == OwnerState.Active) {
                        State = OwnerState.WindowDestroyed;
                        ReferenceCount = 0;
                        IsRegistered = false;
                    }
                }
                ScheduleStateRemoval();
            }
            handled = false;
            return 0;
        }

        /*
         * Keep an idle state weakly keyed while its source remains alive. A factory's earlier
         * Disposed handler can release the final lease before this registry's handler runs;
         * the retained state lets later Disposed callbacks observe the terminal tombstone.
         */
        private void ReleaseOnDispatcher() {
            lock (Locker) {
                var leaseIsNoLongerActive = State != OwnerState.Active || ReferenceCount == 0;
                if (leaseIsNoLongerActive) {
                    return;
                }
                ReferenceCount--;
                var listenerHasOtherLeases = ReferenceCount != 0 || !IsRegistered;
                if (listenerHasOtherLeases) {
                    return;
                }
                var registrationIsAlreadyChanging = IsAdding || IsRemoving;
                if (registrationIsAlreadyChanging) {
                    throw new InvalidOperationException("Clipboard listener registration is already changing.");
                }
                IsRemoving = true;
                IsRegistered = false;
            }

            if (SourceIsUnavailable(out var source)) {
                lock (Locker) {
                    State = OwnerState.WindowDestroyed;
                    IsRemoving = false;
                }
                if (source is not null) {
                    try {
                        Detach(source, removeHook: true);
                    }
                    finally {
                        ScheduleStateRemoval();
                    }
                }
                return;
            }

            bool windowIsLive;
            try {
                windowIsLive = RemoveListenerIfWindowIsLive();
            }
            catch {
                lock (Locker) {
                    IsRemoving = false;
                    var ownerRemainsActive = State == OwnerState.Active;
                    if (ownerRemainsActive) {
                        IsRegistered = true;
                    }
                }
                throw;
            }

            lock (Locker) {
                IsRemoving = false;
                var windowWasDestroyed = !windowIsLive && State == OwnerState.Active;
                if (windowWasDestroyed) {
                    State = OwnerState.WindowDestroyed;
                }
            }
            if (!windowIsLive) {
                try {
                    Detach(source, removeHook: true);
                }
                finally {
                    ScheduleStateRemoval();
                }
            }
        }

        private void InvalidateAfterDispatcherShutdown() {
            var error = EndOwner(OwnerState.DispatcherShuttingDown, removeRegisteredListener: false,
                removeStateImmediately: true, removeHook: false, scheduleStateRemoval: false);
            RecordDeferredCleanupError(error);
        }

        private void Release() {
            if (Dispatcher.CheckAccess()) {
                ReleaseOnDispatcher();
                return;
            }
            if (Dispatcher.HasShutdownFinished) {
                InvalidateAfterDispatcherShutdown();
                return;
            }
            if (Dispatcher.HasShutdownStarted) {
                return;
            }

            try {
                Dispatcher.Invoke(
                    ReleaseOnDispatcher,
                    DispatcherPriority.Send,
                    CancellationToken.None,
                    DispatcherOperationTimeout);
            }
            catch (TaskCanceledException) when (DispatcherIsShuttingDown(Dispatcher)) {
                if (Dispatcher.HasShutdownFinished) {
                    InvalidateAfterDispatcherShutdown();
                }
            }
            catch (InvalidOperationException) when (DispatcherIsShuttingDown(Dispatcher)) {
                if (Dispatcher.HasShutdownFinished) {
                    InvalidateAfterDispatcherShutdown();
                }
            }
        }

        private void AddFailed() {
            HwndSource source;
            lock (Locker) {
                IsAdding = false;
                var ownerIsActive = State == OwnerState.Active;
                var sourceIsReachable = Source.TryGetTarget(out source);
                var failedAddCanBeRolledBack = ownerIsActive && sourceIsReachable;
                if (!failedAddCanBeRolledBack) {
                    return;
                }
            }

            try {
                Detach(source, removeHook: true);
            }
            catch {
            }
            Registry.RemoveState(source, this);
        }

        internal ListenerState(ClipboardListenerRegistry registry,
                               IClipboardNativeApi api,
                               HwndSource source,
                               nint hwnd) {
            Registry = registry;
            Api = api;
            Source = new WeakReference<HwndSource>(source);
            Dispatcher = source.Dispatcher;
            Hwnd = hwnd;
        }

        internal void Attach(HwndSource source) {
            try {
                source.Disposed += OnSourceDisposed;
                HasDisposedHandler = true;
                Dispatcher.ShutdownStarted += OnDispatcherShutdownStarted;
                HasShutdownHandler = true;
                source.AddHook(OnSourceHook);
                HasHook = true;
            }
            catch {
                try {
                    Detach(source, removeHook: true);
                }
                catch {
                }
                throw;
            }
        }

        internal IClipboardListenerLease Acquire() {
            lock (Locker) {
                var ownerIsUnavailable = State != OwnerState.Active;
                if (ownerIsUnavailable) {
                    throw new ObjectDisposedException(nameof(HwndSource));
                }
                var registrationIsAlreadyChanging = IsAdding || IsRemoving;
                if (registrationIsAlreadyChanging) {
                    throw new InvalidOperationException("Clipboard listener registration is already changing.");
                }
                if (IsRegistered) {
                    ReferenceCount++;
                    return new Lease(this);
                }
                IsAdding = true;
            }

            try {
                Api.AddClipboardFormatListener(Hwnd);
            }
            catch {
                AddFailed();
                throw;
            }

            var sourceIsUnavailable = SourceIsUnavailable(out var source);
            var dispatcherIsShuttingDown = DispatcherIsShuttingDown(Dispatcher);
            bool shouldRemove;
            lock (Locker) {
                IsAdding = false;
                var sourceCanAcquireListener = State == OwnerState.Active && !sourceIsUnavailable &&
                    !dispatcherIsShuttingDown;
                if (sourceCanAcquireListener) {
                    IsRegistered = true;
                    ReferenceCount = 1;
                    return new Lease(this);
                }

                if (State == OwnerState.Active) {
                    State = dispatcherIsShuttingDown
                        ? OwnerState.DispatcherShuttingDown
                        : source is null || source.IsDisposed
                            ? OwnerState.WindowDestroyed
                            : OwnerState.WindowClosing;
                }
                var ownerDispatcherIsClosing = State == OwnerState.DispatcherShuttingDown;
                var closingSourceStillOwnsLiveWindow = State == OwnerState.WindowClosing &&
                    source is not null && !source.IsDisposed && Api.IsWindow(Hwnd);
                shouldRemove = ownerDispatcherIsClosing || closingSourceStillOwnsLiveWindow;
                ReferenceCount = 0;
                IsRegistered = false;
            }

            Exception removalError = null;
            if (shouldRemove) {
                try {
                    RemoveListenerIfWindowIsLive();
                }
                catch (Exception exception) {
                    removalError = exception;
                }
            }

            var sourceIsClosing = State is OwnerState.WindowClosing or OwnerState.WindowDestroyed;
            if (sourceIsClosing) {
                ScheduleStateRemoval();
            }
            else if (source is not null) {
                try {
                    Detach(source, removeHook: !source.IsDisposed);
                }
                catch (Exception exception) {
                    var hasEarlierCleanupError = removalError is not null;
                    removalError = hasEarlierCleanupError
                        ? new AggregateException(removalError, exception)
                        : exception;
                }
                Registry.RemoveState(source, this);
            }

            if (removalError is not null) {
                throw removalError;
            }
            throw new ObjectDisposedException(nameof(HwndSource));
        }

        private sealed class Lease : IClipboardListenerLease {
            private readonly ListenerState State;
            private int Disposed;

            internal Lease(ListenerState state) {
                State = state;
            }

            public void Dispose() {
                if (Interlocked.Exchange(ref Disposed, 1) != 0) {
                    return;
                }
                var errors = new List<Exception>();
                try {
                    State.Release();
                }
                catch (Exception exception) {
                    errors.Add(exception);
                }
                var deferredError = State.TakeDeferredCleanupError();
                if (deferredError is not null) {
                    errors.Add(deferredError);
                }
                if (errors.Count == 1) {
                    throw errors[0];
                }
                if (errors.Count > 1) {
                    throw new AggregateException("Clipboard listener release failed.", errors);
                }
            }

            public Exception DisposeForOwnerClosure(bool dispatcherIsClosing) {
                if (Interlocked.Exchange(ref Disposed, 1) != 0) {
                    return null;
                }
                var finalState = dispatcherIsClosing
                    ? OwnerState.DispatcherShuttingDown
                    : OwnerState.WindowClosing;
                var ownerCleanupError = State.EndOwner(finalState, removeRegisteredListener: true,
                    removeStateImmediately: false, removeHook: true,
                    scheduleStateRemoval: !dispatcherIsClosing);
                var deferredCleanupError = State.TakeDeferredCleanupError();
                if (ownerCleanupError is null) {
                    return deferredCleanupError;
                }
                if (deferredCleanupError is null) {
                    return ownerCleanupError;
                }
                return new AggregateException("Clipboard listener owner cleanup failed.",
                    ownerCleanupError, deferredCleanupError);
            }
        }

        private enum OwnerState {
            Active,
            WindowClosing,
            WindowDestroyed,
            DispatcherShuttingDown,
        }
    }
}
