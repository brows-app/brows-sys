using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Brows.Sys;

internal sealed class SystemMessengerSet : ISystemMessengerSet {
    private async IAsyncEnumerable<ISystemMessage>
    EnumerateSystemMessages(object window, [EnumeratorCancellation] CancellationToken cancellationToken) {
        var factories = Factories;
        if (factories is null) {
            yield break;
        }
        var messageChannel = Channel.CreateUnbounded<ISystemMessage>(new() {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false,
        });
        var factoryCompletionChannel = Channel.CreateUnbounded<FactoryCompletion>(new() {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false,
        });
        var lifetime = new SystemMessengerSetLifetime(messageChannel.Writer, messageChannel.Reader);
        var factoryTasks = new List<Task<ISystemMessenger>>();
        var primaryException = default(Exception);
        void CheckCancellation() {
            if (!cancellationToken.IsCancellationRequested) {
                return;
            }
            throw (primaryException = new OperationCanceledException(cancellationToken));
        }
        try {
            foreach (var factory in factories) {
                CheckCancellation();

                Task<ISystemMessenger> factoryTask;
                try {
                    factoryTask = factory is null ?
                        Task.FromResult<ISystemMessenger>(null) :
                        factory.CreateSystemMessenger(window, cancellationToken);
                }
                catch (Exception exception) {
                    primaryException = exception;
                    throw;
                }
                if (factoryTask is null) {
                    factoryTask = Task.FromResult<ISystemMessenger>(null);
                }
                factoryTasks.Add(factoryTask);
                _ = lifetime.ObserveFactoryAsync(factoryTask, factoryCompletionChannel.Writer);
            }

            var messageReader = messageChannel.Reader;
            var factoryCompletionReader = factoryCompletionChannel.Reader;
            var completedFactoryCount = 0;
            var messageWaitTask = messageReader.WaitToReadAsync(cancellationToken).AsTask();
            var factoryCompletionWaitTask =
                factoryCompletionReader.WaitToReadAsync(CancellationToken.None).AsTask();
            for (; ; ) {
                CheckCancellation();

                if (factoryCompletionWaitTask.IsCompleted) {
                    var completionIsAvailable = await factoryCompletionWaitTask.ConfigureAwait(false);
                    if (completionIsAvailable != true) {
                        yield break;
                    }
                    while (factoryCompletionReader.TryRead(out var completion)) {
                        completedFactoryCount++;
                        if (completion.Error is not null) {
                            primaryException = completion.Error;
                            ExceptionDispatchInfo.Capture(completion.Error).Throw();
                        }
                    }
                    factoryCompletionWaitTask =
                        factoryCompletionReader.WaitToReadAsync(CancellationToken.None).AsTask();
                }
                if (messageWaitTask.IsCompleted) {
                    try {
                        var messageIsAvailable = await messageWaitTask.ConfigureAwait(false);
                        if (messageIsAvailable != true) {
                            yield break;
                        }
                    }
                    catch (OperationCanceledException exception) {
                        primaryException = exception;
                        throw;
                    }
                    messageWaitTask = messageReader.WaitToReadAsync(cancellationToken).AsTask();
                }
                if (messageReader.TryRead(out var message)) {
                    CheckCancellation();
                    yield return message;
                    continue;
                }
                var allFactoriesCompletedWithoutMessengers = completedFactoryCount == factoryTasks.Count &&
                                                             lifetime.MessengerCount == 0;
                if (allFactoriesCompletedWithoutMessengers) {
                    yield break;
                }
                await Task.WhenAny(messageWaitTask, factoryCompletionWaitTask).ConfigureAwait(false);
                CheckCancellation();
            }
        }
        finally {
            factoryCompletionChannel.Writer.TryComplete();
            while (factoryCompletionChannel.Reader.TryRead(out _)) {
            }
            var cleanupException = default(AggregateException);
            try {
                cleanupException = lifetime.Cleanup(factoryTasks);
            }
            finally {
                factoryTasks.Clear();
            }
            var cleanupFailureShouldBeReported = primaryException is null && cleanupException is not null;
            if (cleanupFailureShouldBeReported) {
                throw cleanupException;
            }
        }
    }

    [ImportRequired]
    internal IReadOnlyList<ISystemMessengerFactory> Factories { get; set; }

    public IAsyncEnumerable<ISystemMessage>
    ReadAllSystemMessages(object window, CancellationToken cancellationToken) {
        return new SystemMessengerSetMessages(this, window, cancellationToken);
    }

    private sealed class SystemMessengerSetMessages : IAsyncEnumerable<ISystemMessage> {
        private readonly SystemMessengerSet Set;
        private readonly object Window;
        private readonly CancellationToken CancellationToken;

        internal SystemMessengerSetMessages(SystemMessengerSet set, object window,
                                            CancellationToken cancellationToken) {
            Set = set ?? throw new ArgumentNullException(nameof(set));
            Window = window;
            CancellationToken = cancellationToken;
        }

        public IAsyncEnumerator<ISystemMessage> GetAsyncEnumerator(CancellationToken cancellationToken) {
            return new Enumerator(Set, Window, CancellationToken, cancellationToken);
        }

        /*
         * The compiler-generated enumerator of EnumerateSystemMessages cannot be
         * disposed while its body is suspended at an await, which is the state a
         * read leaves behind while it is outstanding. This wrapper keeps every
         * cleanup step in the iterator body reachable from DisposeAsync: the
         * linked cancellation source is canceled first, so an outstanding read
         * wakes up and lets the iterator run its own cleanup, and the underlying
         * enumerator is disposed only after the abandoned read has settled.
         */
        private sealed class Enumerator : IAsyncEnumerator<ISystemMessage> {
            private readonly CancellationTokenSource CancellationSource;
            private readonly IAsyncEnumerator<ISystemMessage> MessengerEnumerator;
            private Task<bool> PendingMoveNext;
            private int Disposed;

            private async ValueTask DisposeAsyncCore() {
                /*
                 * Cancel first: when a read is outstanding, the cancellation
                 * wakes the underlying enumeration so its own cleanup runs, and
                 * the abandoned read completes with a cancellation error.
                 */
                CancellationSource.Cancel();
                var moveNext = Volatile.Read(ref PendingMoveNext);
                var moveNextIsOutstanding = moveNext is not null && !moveNext.IsCompleted;
                if (moveNextIsOutstanding) {
                    try {
                        await moveNext.ConfigureAwait(false);
                    }
                    catch (Exception) {
                        /*
                         * The abandoned read reports cancellation or its own
                         * delivery error to the caller that started it.
                         * Disposal continues with the cleanup.
                         */
                    }
                }
                try {
                    await MessengerEnumerator.DisposeAsync().ConfigureAwait(false);
                }
                finally {
                    CancellationSource.Dispose();
                }
            }

            internal Enumerator(SystemMessengerSet set, object window,
                                CancellationToken parameterToken, CancellationToken enumeratorToken) {
                CancellationSource = CancellationTokenSource.CreateLinkedTokenSource(parameterToken, enumeratorToken);
                MessengerEnumerator = set
                    .EnumerateSystemMessages(window, CancellationSource.Token)
                    .GetAsyncEnumerator(CancellationSource.Token);
            }

            public ISystemMessage Current {
                get {
                    return MessengerEnumerator.Current;
                }
            }

            public ValueTask<bool> MoveNextAsync() {
                var enumeratorIsDisposed = Volatile.Read(ref Disposed) != 0;
                if (enumeratorIsDisposed) {
                    return ValueTask.FromResult(false);
                }
                var moveNext = MessengerEnumerator.MoveNextAsync().AsTask();
                Volatile.Write(ref PendingMoveNext, moveNext);
                return new ValueTask<bool>(moveNext);
            }

            public ValueTask DisposeAsync() {
                if (Interlocked.Exchange(ref Disposed, 1) != 0) {
                    return ValueTask.CompletedTask;
                }
                return DisposeAsyncCore();
            }
        }
    }

    private sealed class SystemMessengerSetLifetime {
        private readonly Lock Locker = new();
        private readonly ChannelReader<ISystemMessage> MessageReader;
        private readonly ChannelWriter<ISystemMessage> MessageWriter;
        private readonly HashSet<ISystemMessenger> KnownMessengers = new(ReferenceEqualityComparer.Instance);
        private readonly List<WeakReference<ISystemMessenger>> DisposedMessengers = [];
        private readonly List<ISystemMessenger> Messengers = [];
        private readonly List<MessengerSubscription> Subscriptions = [];
        private readonly TaskCompletionSource CleanupCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool IsClosing;

        private async Task RegisterMessengerAsync(ISystemMessenger messenger) {
            var shouldDispose = false;
            lock (Locker) {
                if (IsClosing) {
                    shouldDispose = true;
                }
                else if (KnownMessengers.Add(messenger)) {
                    void handler(object source, SystemMessageEventArgs eventArgs) {
                        var message = eventArgs?.Message;
                        if (message is not null) {
                            MessageWriter.TryWrite(message);
                        }
                    }
                    Messengers.Add(messenger);
                    Subscriptions.Add(new MessengerSubscription(messenger, handler));
                    messenger.SystemMessaged += handler;
                }
            }
            if (shouldDispose) {
                await CleanupCompleted.Task.ConfigureAwait(false);
                DisposeMessenger(messenger, null);
            }
        }

        private void DisposeMessenger(ISystemMessenger messenger, List<Exception> cleanupExceptions) {
            lock (Locker) {
                var messengerWasDisposed = false;
                for (var index = DisposedMessengers.Count - 1; index >= 0; index--) {
                    var weakReferenceIsAlive = DisposedMessengers[index].TryGetTarget(out var disposedMessenger);
                    if (weakReferenceIsAlive != true) {
                        DisposedMessengers.RemoveAt(index);
                        continue;
                    }
                    var isSameMessenger = ReferenceEquals(disposedMessenger, messenger);
                    if (isSameMessenger) {
                        messengerWasDisposed = true;
                    }
                }
                if (messengerWasDisposed) {
                    return;
                }
                DisposedMessengers.Add(new WeakReference<ISystemMessenger>(messenger));
            }
            try {
                messenger.Dispose();
            }
            catch (Exception exception) {
                if (cleanupExceptions is not null) {
                    cleanupExceptions.Add(exception);
                }
            }
        }

        internal int MessengerCount {
            get {
                lock (Locker) {
                    return Messengers.Count;
                }
            }
        }

        internal SystemMessengerSetLifetime(ChannelWriter<ISystemMessage> messageWriter,
                                            ChannelReader<ISystemMessage> messageReader) {
            MessageWriter = messageWriter ?? throw new ArgumentNullException(nameof(messageWriter));
            MessageReader = messageReader ?? throw new ArgumentNullException(nameof(messageReader));
        }

        internal async Task ObserveFactoryAsync(Task<ISystemMessenger> factoryTask,
                                                ChannelWriter<FactoryCompletion> factoryCompletionWriter) {
            if (factoryTask is null) {
                throw new ArgumentNullException(nameof(factoryTask));
            }
            if (factoryCompletionWriter is null) {
                throw new ArgumentNullException(nameof(factoryCompletionWriter));
            }
            var error = default(Exception);
            try {
                var messenger = await factoryTask.ConfigureAwait(false);
                if (messenger is not null) {
                    await RegisterMessengerAsync(messenger).ConfigureAwait(false);
                }
            }
            catch (Exception exception) {
                error = exception;
            }
            factoryCompletionWriter.TryWrite(new FactoryCompletion(error));
        }

        internal AggregateException Cleanup(IReadOnlyList<Task<ISystemMessenger>> factoryTasks) {
            if (factoryTasks is null) {
                throw new ArgumentNullException(nameof(factoryTasks));
            }
            MessengerSubscription[] subscriptions;
            ISystemMessenger[] messengers;
            var cleanupExceptions = new List<Exception>();
            lock (Locker) {
                if (IsClosing) {
                    return null;
                }
                IsClosing = true;
                try {
                    MessageWriter.TryComplete();
                }
                catch (Exception exception) {
                    cleanupExceptions.Add(exception);
                }
                while (MessageReader.TryRead(out _)) {
                }
                foreach (var factoryTask in factoryTasks) {
                    if (factoryTask.IsCompletedSuccessfully) {
                        var messenger = factoryTask.Result;
                        var messengerIsNew = messenger is not null && KnownMessengers.Add(messenger);
                        if (messengerIsNew) {
                            Messengers.Add(messenger);
                        }
                    }
                }
                subscriptions = [.. Subscriptions];
                messengers = [.. Messengers];
            }
            try {
                foreach (var subscription in subscriptions) {
                    try {
                        subscription.Messenger.SystemMessaged -= subscription.Handler;
                    }
                    catch (Exception exception) {
                        cleanupExceptions.Add(exception);
                    }
                }
                foreach (var messenger in messengers) {
                    DisposeMessenger(messenger, cleanupExceptions);
                }
            }
            finally {
                lock (Locker) {
                    KnownMessengers.Clear();
                    Messengers.Clear();
                    Subscriptions.Clear();
                }
                CleanupCompleted.TrySetResult();
            }
            if (cleanupExceptions.Count == 0) {
                return null;
            }
            return new AggregateException(
                "One or more system messenger cleanup operations failed.", cleanupExceptions);
        }
    }

    private sealed record FactoryCompletion(Exception Error);

    private sealed record MessengerSubscription(ISystemMessenger Messenger, SystemMessageEventHandler Handler);
}
