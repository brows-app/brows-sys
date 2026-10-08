using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Brows;

internal sealed class DispatcherThread : IDisposable {
    private readonly TaskCompletionSource<Dispatcher> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread Thread;

    private void Run() {
        Ready.SetResult(Dispatcher.CurrentDispatcher);
        Dispatcher.Run();
    }

    internal Dispatcher Dispatcher { get; }

    internal DispatcherThread() {
        Thread = new Thread(Run) { IsBackground = true };
        Thread.SetApartmentState(ApartmentState.STA);
        Thread.Start();
        Dispatcher = Ready.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    }

    internal T Invoke<T>(Func<T> callback) {
        return Dispatcher.Invoke(callback, DispatcherPriority.Normal, CancellationToken.None, TimeSpan.FromSeconds(10));
    }

    internal void Invoke(Action callback) {
        Dispatcher.Invoke(callback, DispatcherPriority.Normal, CancellationToken.None, TimeSpan.FromSeconds(10));
    }

    public void Dispose() {
        if (!Dispatcher.HasShutdownStarted) {
            Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        }
        if (!Thread.Join(TimeSpan.FromSeconds(10))) {
            throw new TimeoutException("The test dispatcher did not stop.");
        }
    }
}
