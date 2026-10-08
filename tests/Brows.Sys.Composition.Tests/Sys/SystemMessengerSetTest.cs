using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Brows.Sys;

[TestFixture]
internal sealed class SystemMessengerSetTest {
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private static SystemMessengerSet CreateMessengerSet(params ISystemMessengerFactory[] factories) {
        return new SystemMessengerSet {
            Factories = factories,
        };
    }

    private static async Task DisposeEnumeratorAsync(IAsyncEnumerator<ISystemMessage> enumerator) {
        await enumerator.DisposeAsync().AsTask().WaitAsync(WaitTimeout);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<ISystemMessage> QueueUnreadMessage(RecordingMessenger messenger) {
        var message = new TestMessage(11);
        var messageReference = new WeakReference<ISystemMessage>(message);
        messenger.Emit(message);
        return messageReference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CollectGarbage() {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Test]
    public async Task EnumeratorDisposalReleasesUnreadBufferedMessagesWhileFactoryRemainsPending() {
        var messenger = new RecordingMessenger();
        var pendingFactory = new TaskCompletionSource<ISystemMessenger>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var messengerSet = CreateMessengerSet(
            new DelegateFactory(() => Task.FromResult<ISystemMessenger>(messenger)),
            new DelegateFactory(() => pendingFactory.Task));
        var enumerator = messengerSet.ReadSystemMessages(new object(), CancellationToken.None).GetAsyncEnumerator();
        var lateMessenger = new RecordingMessenger();
        var enumeratorCleanupStarted = false;

        try {
            var moveNext = enumerator.MoveNextAsync().AsTask();
            await messenger.Subscribed.Task.WaitAsync(WaitTimeout);
            messenger.Emit(new TestMessage(10));
            Assert.That(await moveNext.WaitAsync(WaitTimeout), Is.True);

            var unreadMessageReference = QueueUnreadMessage(messenger);
            enumeratorCleanupStarted = true;
            await DisposeEnumeratorAsync(enumerator);

            CollectGarbage();
            var unreadMessageIsRetained = unreadMessageReference.TryGetTarget(out _);
            Assert.That(unreadMessageIsRetained, Is.False);
        }
        finally {
            if (!enumeratorCleanupStarted) {
                await DisposeEnumeratorAsync(enumerator);
            }

            pendingFactory.TrySetResult(lateMessenger);
            await lateMessenger.Disposed.WaitAsync(WaitTimeout);
        }
    }

    [Test]
    public async Task EnumeratorDisposalUnsubscribesAndDisposesMessenger() {
        var messenger = new RecordingMessenger();
        var messengerSet = CreateMessengerSet(new DelegateFactory(() => Task.FromResult<ISystemMessenger>(messenger)));
        var enumerator = messengerSet.ReadSystemMessages(new object(), CancellationToken.None).GetAsyncEnumerator();

        var moveNext = enumerator.MoveNextAsync().AsTask();
        await messenger.Subscribed.Task.WaitAsync(WaitTimeout);
        var message = new TestMessage(1);
        messenger.Emit(message);

        Assert.That(await moveNext.WaitAsync(WaitTimeout), Is.True);
        Assert.That(enumerator.Current, Is.SameAs(message));

        await DisposeEnumeratorAsync(enumerator);

        Assert.That(messenger.SubscriptionCount, Is.Zero);
        Assert.That(messenger.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task CompletedFactoryIsSubscribedBeforeOtherFactoryCompletes() {
        var messenger = new RecordingMessenger();
        var pendingFactory = new TaskCompletionSource<ISystemMessenger>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var messengerSet = CreateMessengerSet(
            new DelegateFactory(() => Task.FromResult<ISystemMessenger>(messenger)),
            new DelegateFactory(() => pendingFactory.Task));
        var enumerator = messengerSet.ReadSystemMessages(new object(), CancellationToken.None).GetAsyncEnumerator();
        var moveNext = enumerator.MoveNextAsync().AsTask();
        var message = new TestMessage(2);
        var lateMessenger = new RecordingMessenger();

        try {
            await messenger.Subscribed.Task.WaitAsync(WaitTimeout);
            messenger.Emit(message);
            Assert.That(await moveNext.WaitAsync(WaitTimeout), Is.True);
            Assert.That(enumerator.Current, Is.SameAs(message));
            await DisposeEnumeratorAsync(enumerator);

            pendingFactory.TrySetResult(lateMessenger);
            await lateMessenger.Disposed.WaitAsync(WaitTimeout);
            Assert.That(lateMessenger.DisposeCount, Is.EqualTo(1));
        }
        catch {
            pendingFactory.TrySetResult(lateMessenger);
            if (!messenger.Subscribed.Task.IsCompleted) {
                await messenger.Subscribed.Task.WaitAsync(WaitTimeout);
            }
            if (!moveNext.IsCompleted) {
                messenger.Emit(message);
                await moveNext.WaitAsync(WaitTimeout);
            }
            await DisposeEnumeratorAsync(enumerator);
            throw;
        }
    }

    [Test]
    public async Task CancellationWhileDrainingMessagesStopsBeforeNextMessageAndCleansUp() {
        var messenger = new RecordingMessenger(throwOnDispose: true);
        var pendingFactory = new TaskCompletionSource<ISystemMessenger>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var messengerSet = CreateMessengerSet(
            new DelegateFactory(() => Task.FromResult<ISystemMessenger>(messenger)),
            new DelegateFactory(() => pendingFactory.Task));
        using var cancellation = new CancellationTokenSource();
        var enumerator = messengerSet.ReadSystemMessages(new object(), cancellation.Token).GetAsyncEnumerator();
        var firstMove = enumerator.MoveNextAsync().AsTask();
        var messages = new[] { new TestMessage(1), new TestMessage(2), new TestMessage(3) };

        await messenger.Subscribed.Task.WaitAsync(WaitTimeout);
        foreach (var message in messages) {
            messenger.Emit(message);
        }

        Assert.That(await firstMove.WaitAsync(WaitTimeout), Is.True);
        Assert.That(enumerator.Current, Is.SameAs(messages[0]));
        cancellation.Cancel();

        await Assert.CatchAsync<OperationCanceledException>(async () => {
            await enumerator.MoveNextAsync().AsTask().WaitAsync(WaitTimeout);
        });

        Assert.That(messenger.SubscriptionCount, Is.Zero);
        Assert.That(messenger.DisposeCount, Is.EqualTo(1));
        var lateMessenger = new RecordingMessenger();
        pendingFactory.TrySetResult(lateMessenger);
        await lateMessenger.Disposed.WaitAsync(WaitTimeout);
        await DisposeEnumeratorAsync(enumerator);
    }

    [Test]
    public async Task EnumeratorDisposalAttemptsEveryCleanupAndReportsFailures() {
        var throwingMessenger = new RecordingMessenger(throwOnUnsubscribe: true, throwOnDispose: true);
        var otherMessenger = new RecordingMessenger();
        var messengerSet = CreateMessengerSet(
            new DelegateFactory(() => Task.FromResult<ISystemMessenger>(throwingMessenger)),
            new DelegateFactory(() => Task.FromResult<ISystemMessenger>(otherMessenger)));
        var enumerator = messengerSet.ReadSystemMessages(new object(), CancellationToken.None).GetAsyncEnumerator();
        var moveNext = enumerator.MoveNextAsync().AsTask();

        await throwingMessenger.Subscribed.Task.WaitAsync(WaitTimeout);
        await otherMessenger.Subscribed.Task.WaitAsync(WaitTimeout);
        throwingMessenger.Emit(new TestMessage(5));
        Assert.That(await moveNext.WaitAsync(WaitTimeout), Is.True);

        var error = await Assert.ThrowsAsync<AggregateException>(async () => {
            await DisposeEnumeratorAsync(enumerator);
        });

        Assert.That(error!.InnerExceptions, Has.Count.EqualTo(2));
        Assert.That(throwingMessenger.UnsubscribeAttemptCount, Is.EqualTo(1));
        Assert.That(throwingMessenger.DisposeCount, Is.EqualTo(1));
        Assert.That(otherMessenger.UnsubscribeAttemptCount, Is.EqualTo(1));
        Assert.That(otherMessenger.SubscriptionCount, Is.Zero);
        Assert.That(otherMessenger.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task FactoryFailureIsObservedBeforeBufferedMessagesAreYielded() {
        var message = new TestMessage(6);
        var messenger = new RecordingMessenger(messageOnSubscribe: message);
        var messengerSet = CreateMessengerSet(
            new DelegateFactory(() => Task.FromResult<ISystemMessenger>(messenger)),
            new DelegateFactory(() => Task.FromException<ISystemMessenger>(
                new InvalidOperationException("Factory failed."))));
        var enumerator = messengerSet.ReadSystemMessages(new object(), CancellationToken.None).GetAsyncEnumerator();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await enumerator.MoveNextAsync().AsTask().WaitAsync(WaitTimeout);
        });

        Assert.That(error!.Message, Is.EqualTo("Factory failed."));
        Assert.That(messenger.SubscriptionCount, Is.Zero);
        Assert.That(messenger.DisposeCount, Is.EqualTo(1));
        await DisposeEnumeratorAsync(enumerator);
    }

    [Test]
    public async Task FactoryFailureCleansUpSuccessfulAndLaterSuccessfulMessengers() {
        var successfulMessenger = new RecordingMessenger(throwOnDispose: true);
        var pendingFactory = new TaskCompletionSource<ISystemMessenger>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var messengerSet = CreateMessengerSet(
            new DelegateFactory(() => Task.FromResult<ISystemMessenger>(successfulMessenger)),
            new DelegateFactory(() => pendingFactory.Task),
            new DelegateFactory(() => Task.FromException<ISystemMessenger>(
                new InvalidOperationException("Factory failed."))));
        var enumerator = messengerSet.ReadSystemMessages(new object(), CancellationToken.None).GetAsyncEnumerator();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await enumerator.MoveNextAsync().AsTask().WaitAsync(WaitTimeout);
        });

        Assert.That(error!.Message, Is.EqualTo("Factory failed."));
        Assert.That(successfulMessenger.SubscriptionCount, Is.Zero);
        Assert.That(successfulMessenger.DisposeCount, Is.EqualTo(1));

        var lateMessenger = new RecordingMessenger(throwOnDispose: true);
        pendingFactory.TrySetResult(lateMessenger);
        await lateMessenger.Disposed.WaitAsync(WaitTimeout);
        Assert.That(lateMessenger.SubscriptionCount, Is.Zero);
        Assert.That(lateMessenger.DisposeCount, Is.EqualTo(1));
        await DisposeEnumeratorAsync(enumerator);
    }

    [Test]
    public async Task CanceledFactoryCleansUpPreviouslyCreatedAndLaterSuccessfulMessengers() {
        var messenger = new RecordingMessenger();
        var pendingFactory = new TaskCompletionSource<ISystemMessenger>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var canceledTask = Task.FromCanceled<ISystemMessenger>(new CancellationToken(true));
        var messengerSet = CreateMessengerSet(
            new DelegateFactory(() => Task.FromResult<ISystemMessenger>(messenger)),
            new DelegateFactory(() => pendingFactory.Task),
            new DelegateFactory(() => canceledTask));
        var enumerator = messengerSet.ReadSystemMessages(new object(), CancellationToken.None).GetAsyncEnumerator();

        await Assert.CatchAsync<OperationCanceledException>(async () => {
            await enumerator.MoveNextAsync().AsTask().WaitAsync(WaitTimeout);
        });

        Assert.That(messenger.SubscriptionCount, Is.Zero);
        Assert.That(messenger.DisposeCount, Is.EqualTo(1));
        var lateMessenger = new RecordingMessenger();
        pendingFactory.TrySetResult(lateMessenger);
        await lateMessenger.Disposed.WaitAsync(WaitTimeout);
        Assert.That(lateMessenger.DisposeCount, Is.EqualTo(1));
        await DisposeEnumeratorAsync(enumerator);
    }

    [Test]
    public async Task SynchronousFactoryFailureCleansUpSuccessfulAndLaterSuccessfulMessengers() {
        var messenger = new RecordingMessenger();
        var pendingFactory = new TaskCompletionSource<ISystemMessenger>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var messengerSet = CreateMessengerSet(
            new DelegateFactory(() => Task.FromResult<ISystemMessenger>(messenger)),
            new DelegateFactory(() => pendingFactory.Task),
            new DelegateFactory(() => throw new InvalidOperationException("Synchronous failure.")));
        var enumerator = messengerSet.ReadSystemMessages(new object(), CancellationToken.None).GetAsyncEnumerator();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await enumerator.MoveNextAsync().AsTask().WaitAsync(WaitTimeout);
        });
        Assert.That(error!.Message, Is.EqualTo("Synchronous failure."));
        Assert.That(messenger.SubscriptionCount, Is.Zero);
        Assert.That(messenger.DisposeCount, Is.EqualTo(1));

        var lateMessenger = new RecordingMessenger();
        pendingFactory.TrySetResult(lateMessenger);
        await lateMessenger.Disposed.WaitAsync(WaitTimeout);
        Assert.That(lateMessenger.DisposeCount, Is.EqualTo(1));
        await DisposeEnumeratorAsync(enumerator);
    }

    [Test]
    public async Task DuplicateMessengerIsSubscribedAndDisposedOnlyOnce() {
        var messenger = new RecordingMessenger();
        var messengerSet = CreateMessengerSet(
            new DelegateFactory(() => Task.FromResult<ISystemMessenger>(messenger)),
            new DelegateFactory(() => Task.FromResult<ISystemMessenger>(messenger)));
        var enumerator = messengerSet.ReadSystemMessages(new object(), CancellationToken.None).GetAsyncEnumerator();
        var moveNext = enumerator.MoveNextAsync().AsTask();

        await messenger.Subscribed.Task.WaitAsync(WaitTimeout);
        messenger.Emit(new TestMessage(4));
        Assert.That(await moveNext.WaitAsync(WaitTimeout), Is.True);
        await DisposeEnumeratorAsync(enumerator);

        Assert.That(messenger.SubscriptionCount, Is.Zero);
        Assert.That(messenger.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DisposeWhileReadIsOutstandingCancelsReadAndCleansUp() {
        var messenger = new RecordingMessenger();
        var messengerSet = CreateMessengerSet(
            new DelegateFactory(() => Task.FromResult<ISystemMessenger>(messenger)));
        var enumerator = messengerSet.ReadSystemMessages(new object(), CancellationToken.None).GetAsyncEnumerator();
        var moveNext = enumerator.MoveNextAsync().AsTask();

        await messenger.Subscribed.Task.WaitAsync(WaitTimeout);
        await Assert.DoesNotThrowAsync(async () => {
            await enumerator.DisposeAsync().AsTask().WaitAsync(WaitTimeout);
        });

        await Assert.CatchAsync<OperationCanceledException>(async () => {
            await moveNext.WaitAsync(WaitTimeout);
        });
        Assert.That(messenger.SubscriptionCount, Is.Zero);
        Assert.That(messenger.DisposeCount, Is.EqualTo(1));

        await Assert.DoesNotThrowAsync(async () => {
            await enumerator.DisposeAsync().AsTask().WaitAsync(WaitTimeout);
        });
        Assert.That(messenger.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GetAsyncEnumeratorTokenCancelsOutstandingReadAndCleansUp() {
        var messenger = new RecordingMessenger();
        var messengerSet = CreateMessengerSet(
            new DelegateFactory(() => Task.FromResult<ISystemMessenger>(messenger)));
        using var cancellation = new CancellationTokenSource();
        var enumerator = messengerSet
            .ReadSystemMessages(new object(), CancellationToken.None)
            .GetAsyncEnumerator(cancellation.Token);
        var moveNext = enumerator.MoveNextAsync().AsTask();

        await messenger.Subscribed.Task.WaitAsync(WaitTimeout);
        cancellation.Cancel();

        await Assert.CatchAsync<OperationCanceledException>(async () => {
            await moveNext.WaitAsync(WaitTimeout);
        });
        Assert.That(messenger.SubscriptionCount, Is.Zero);
        Assert.That(messenger.DisposeCount, Is.EqualTo(1));
        await DisposeEnumeratorAsync(enumerator);
    }

    private sealed class DelegateFactory : ISystemMessengerFactory {
        private readonly Func<Task<ISystemMessenger>> CreateMessenger;

        public DelegateFactory(Func<Task<ISystemMessenger>> createMessenger) {
            CreateMessenger = createMessenger;
        }

        public Task<ISystemMessenger> CreateSystemMessenger(object window, CancellationToken cancellationToken) {
            return CreateMessenger();
        }
    }

    private sealed class RecordingMessenger : ISystemMessenger {
        private readonly bool ThrowOnUnsubscribe;
        private readonly bool ThrowOnDispose;
        private readonly ISystemMessage MessageOnSubscribe;
        private SystemMessageEventHandler SystemMessagedHandlers;
        private int SubscriptionCountValue;
        private int UnsubscribeAttemptCountValue;
        private int DisposeCountValue;

        public event SystemMessageEventHandler SystemMessaged {
            add {
                SystemMessagedHandlers += value;
                Interlocked.Increment(ref SubscriptionCountValue);
                Subscribed.TrySetResult();
                if (MessageOnSubscribe is not null) {
                    Emit(MessageOnSubscribe);
                }
            }
            remove {
                SystemMessagedHandlers -= value;
                Interlocked.Increment(ref UnsubscribeAttemptCountValue);
                Interlocked.Decrement(ref SubscriptionCountValue);
                if (ThrowOnUnsubscribe) {
                    throw new InvalidOperationException("Unsubscribe failed.");
                }
            }
        }

        public TaskCompletionSource Subscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DisposedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Disposed {
            get {
                return DisposedSignal.Task;
            }
        }

        public int SubscriptionCount {
            get {
                return Volatile.Read(ref SubscriptionCountValue);
            }
        }

        public int UnsubscribeAttemptCount {
            get {
                return Volatile.Read(ref UnsubscribeAttemptCountValue);
            }
        }

        public int DisposeCount {
            get {
                return Volatile.Read(ref DisposeCountValue);
            }
        }

        public RecordingMessenger(
            bool throwOnUnsubscribe = false,
            bool throwOnDispose = false,
            ISystemMessage messageOnSubscribe = null) {
            ThrowOnUnsubscribe = throwOnUnsubscribe;
            ThrowOnDispose = throwOnDispose;
            MessageOnSubscribe = messageOnSubscribe;
        }

        public void Dispose() {
            Interlocked.Increment(ref DisposeCountValue);
            DisposedSignal.TrySetResult();
            if (ThrowOnDispose) {
                throw new InvalidOperationException("Disposal failed.");
            }
        }

        public void Emit(ISystemMessage message) {
            SystemMessagedHandlers?.Invoke(this, new SystemMessageEventArgs(message));
        }
    }

    private sealed record TestMessage(int Id) : SystemMessage {
        public override SystemMessageKind SystemMessageKind {
            get {
                return SystemMessageKind.Device;
            }
        }
    }
}
