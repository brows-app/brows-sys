using Brows.Sys;
using Brows.Sys.Messages.DeviceMessages.DeviceChanges;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Brows;

[TestFixture]
internal sealed class EventLogControllerTest {
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task StartsOneReaderAndStopsItWithCleanup() {
        using var dispatcher = new DispatcherThread();
        var set = new ControlledMessengerSet();
        var log = new EventLog();
        var controller = new EventLogController(set, new object(), log, dispatcher.Dispatcher);
        dispatcher.Invoke(() => {
            controller.Start();
            controller.Start();
        });
        await set.Started.Task.WaitAsync(Timeout);
        Assert.That(set.ReadCount, Is.EqualTo(1));

        var stop = dispatcher.Invoke(controller.StopAsync);
        await stop.WaitAsync(Timeout);
        await set.Finished.Task.WaitAsync(Timeout);
        Assert.That(set.CancellationObserved, Is.True);
        Assert.That(dispatcher.Invoke(() => log.Status), Is.EqualTo("Stopped"));
        set.Emit(new DeviceArrival());
        await dispatcher.Invoke(controller.StopAsync).WaitAsync(Timeout);
        Assert.That(dispatcher.Invoke(() => log.Count), Is.Zero);
    }

    [Test]
    public async Task UpdatesLogOnTheDispatcher() {
        using var dispatcher = new DispatcherThread();
        var set = new ControlledMessengerSet();
        var log = new EventLog();
        var controller = new EventLogController(set, new object(), log, dispatcher.Dispatcher);
        var updated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        log.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(EventLog.Count)) {
                updated.TrySetResult(dispatcher.Dispatcher.CheckAccess());
            }
        };
        dispatcher.Invoke(controller.Start);
        try {
            await set.Started.Task.WaitAsync(Timeout);
            set.Emit(new DeviceArrival());
            Assert.That(await updated.Task.WaitAsync(Timeout), Is.True);
            Assert.That(dispatcher.Invoke(() => log.Count), Is.EqualTo(1));
        }
        finally {
            await dispatcher.Invoke(controller.StopAsync).WaitAsync(Timeout);
        }
    }

    [Test]
    public async Task KeepsDrainingWhilePausedAndOnlyCapturesFutureEventsOnResume() {
        using var dispatcher = new DispatcherThread();
        var set = new ControlledMessengerSet();
        var log = new EventLog();
        var controller = new EventLogController(set, new object(), log, dispatcher.Dispatcher);
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        log.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(EventLog.Count)) {
                updated.TrySetResult();
            }
        };
        dispatcher.Invoke(() => {
            controller.Start();
            log.TogglePause();
        });
        try {
            set.Emit(new DeviceArrival());
            await set.Drained.Task.WaitAsync(Timeout);
            Assert.That(dispatcher.Invoke(() => log.Count), Is.Zero);
            dispatcher.Invoke(log.TogglePause);
            set.Emit(new DeviceRemovalComplete());
            await updated.Task.WaitAsync(Timeout);
            dispatcher.Invoke(() => {
                Assert.That(log.Count, Is.EqualTo(1));
                Assert.That(log.Entries[0].ChangeKind, Is.EqualTo("RemovalComplete"));
            });
        }
        finally {
            await dispatcher.Invoke(controller.StopAsync).WaitAsync(Timeout);
        }
    }

    [Test]
    public async Task ReportsStreamErrorsAndStillStops() {
        using var dispatcher = new DispatcherThread();
        var set = new ControlledMessengerSet();
        var log = new EventLog();
        var controller = new EventLogController(set, new object(), log, dispatcher.Dispatcher);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        log.PropertyChanged += (_, e) => {
            var errorReported = e.PropertyName == nameof(EventLog.Status) && log.Status == "Error";
            if (errorReported) {
                failed.TrySetResult();
            }
        };
        dispatcher.Invoke(controller.Start);
        set.Fail(new InvalidOperationException("Test failure."));
        await failed.Task.WaitAsync(Timeout);
        Assert.That(dispatcher.Invoke(() => log.Error), Is.EqualTo("Test failure."));
        await dispatcher.Invoke(controller.StopAsync).WaitAsync(Timeout);
        Assert.That(set.Finished.Task.IsCompleted, Is.True);
    }

    [Test]
    public async Task StopBeforeStartPreventsCreation() {
        using var dispatcher = new DispatcherThread();
        var set = new ControlledMessengerSet();
        var log = new EventLog();
        var controller = new EventLogController(set, new object(), log, dispatcher.Dispatcher);
        await dispatcher.Invoke(controller.StopAsync).WaitAsync(Timeout);
        dispatcher.Invoke(controller.Start);
        Assert.That(set.ReadCount, Is.Zero);
    }

    private sealed class ControlledMessengerSet : ISystemMessengerSet {
        private readonly Channel<ISystemMessage> Messages = Channel.CreateUnbounded<ISystemMessage>();

        private async IAsyncEnumerable<ISystemMessage> Read(
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            Started.TrySetResult();
            try {
                await foreach (var message in Messages.Reader.ReadAllAsync(cancellationToken)) {
                    yield return message;
                    Drained.TrySetResult();
                }
            }
            finally {
                CancellationObserved = cancellationToken.IsCancellationRequested;
                Finished.TrySetResult();
            }
        }

        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int ReadCount { get; private set; }

        internal bool CancellationObserved { get; private set; }

        internal void Emit(ISystemMessage message) {
            Messages.Writer.TryWrite(message);
        }

        internal void Fail(Exception exception) {
            Messages.Writer.TryComplete(exception);
        }

        public IAsyncEnumerable<ISystemMessage> ReadAllSystemMessages(
            object window, CancellationToken cancellationToken) {
            ReadCount++;
            return Read(cancellationToken);
        }
    }
}
