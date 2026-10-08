using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Brows;

internal sealed class DispatcherThread : IDisposable {
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly TaskCompletionSource<Dispatcher> DispatcherCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Thread Thread;

    private void RunDispatcher() {
        DispatcherCompletion.TrySetResult(Dispatcher.CurrentDispatcher);
        Dispatcher.Run();
    }

    internal Dispatcher Dispatcher { get; }

    internal int ThreadId => Thread.ManagedThreadId;

    internal bool IsAlive => Thread.IsAlive;

    internal bool IsForeground => !Thread.IsBackground;

    internal DispatcherThread() {
        Thread = new Thread(RunDispatcher);
        Thread.SetApartmentState(ApartmentState.STA);
        Thread.Start();
        try {
            Dispatcher = DispatcherCompletion.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
        }
        catch {
            if (Thread.IsAlive) {
                Thread.IsBackground = true;
            }
            if (DispatcherCompletion.Task.IsCompletedSuccessfully) {
                DispatcherCompletion.Task.Result.BeginInvokeShutdown(DispatcherPriority.Send);
                Thread.Join(Timeout);
            }

            throw;
        }
    }

    internal T Invoke<T>(Func<T> callback) {
        return Dispatcher.Invoke(callback, DispatcherPriority.Send, CancellationToken.None, Timeout);
    }

    internal void Invoke(Action callback) {
        Dispatcher.Invoke(callback, DispatcherPriority.Send, CancellationToken.None, Timeout);
    }

    internal void BeginShutdown() {
        var dispatcherHasNotStartedShutdown = !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished;
        if (dispatcherHasNotStartedShutdown) {
            Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        }
    }

    internal static HwndSource CreateSource(string title) {
        return new HwndSource(new HwndSourceParameters(title) {
            Width = 1,
            Height = 1,
            WindowStyle = 0,
        });
    }

    public void Dispose() {
        BeginShutdown();
        var dispatcherThreadStopped = Thread.Join(Timeout);
        if (!dispatcherThreadStopped) {
            var dispatcherThreadRemainsAlive = Thread.IsAlive;
            if (dispatcherThreadRemainsAlive) {
                Thread.IsBackground = true;
            }
            throw new TimeoutException("The WPF test dispatcher did not stop.");
        }
    }
}