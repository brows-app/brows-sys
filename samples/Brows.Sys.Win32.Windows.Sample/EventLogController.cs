using Brows.Sys;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Brows;

internal sealed class EventLogController {
    private readonly ISystemMessengerSet MessengerSet;
    private readonly object Window;
    private readonly EventLog Log;
    private readonly Dispatcher Dispatcher;
    private readonly CancellationTokenSource CancellationSource = new();

    private Task ReadingTask = Task.CompletedTask;
    private Task StopTask;
    private bool Started;
    private int StopRequested;

    private async Task ReadAsync() {
        var cancellationToken = CancellationSource.Token;
        try {
            await foreach (var message in MessengerSet.ReadAllSystemMessages(Window, cancellationToken)
                .ConfigureAwait(false)) {
                var timestamp = DateTimeOffset.Now;
                await Dispatcher.InvokeAsync(() => Log.Append(message, timestamp),
                    DispatcherPriority.Background, cancellationToken).Task.ConfigureAwait(false);
            }
            if (Volatile.Read(ref StopRequested) == 0) {
                await Dispatcher.InvokeAsync(() => {
                    if (Volatile.Read(ref StopRequested) == 0) {
                        Log.Stopped();
                    }
                }).Task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
        }
        catch (Exception exception) {
            if (Volatile.Read(ref StopRequested) == 0) {
                await Dispatcher.InvokeAsync(() => {
                    if (Volatile.Read(ref StopRequested) == 0) {
                        Log.Fail(exception);
                    }
                }).Task.ConfigureAwait(false);
            }
        }
    }

    private async Task StopAsyncCore() {
        Interlocked.Exchange(ref StopRequested, 1);
        Log.Stopping();
        try {
            try {
                CancellationSource.Cancel();
            }
            catch (Exception exception) {
                Log.Fail(exception);
            }
            await ReadingTask;
        }
        finally {
            CancellationSource.Dispose();
            Log.Stopped();
        }
    }

    internal EventLogController(ISystemMessengerSet messengerSet, object window, EventLog log, Dispatcher dispatcher) {
        MessengerSet = messengerSet ?? throw new ArgumentNullException(nameof(messengerSet));
        Window = window ?? throw new ArgumentNullException(nameof(window));
        Log = log ?? throw new ArgumentNullException(nameof(log));
        Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    internal void Start() {
        Dispatcher.VerifyAccess();
        var cannotStart = Started || Volatile.Read(ref StopRequested) != 0;
        if (cannotStart) {
            return;
        }
        Started = true;
        Log.Listening();
        ReadingTask = ReadAsync();
    }

    internal Task StopAsync() {
        Dispatcher.VerifyAccess();
        return StopTask ??= StopAsyncCore();
    }
}
