using System;

namespace Brows;

[TestFixture]
internal sealed class DispatcherThreadTest {
    [Test]
    public void DispatcherThread_DisposesIdempotentlyAfterScopeThrows() {
        DispatcherThread dispatcher = null;
        var injectedFailure = new InvalidOperationException("Injected dispatcher scope failure.");
        try {
            var error = Assert.Throws<InvalidOperationException>(() => {
                using var scopedDispatcher = new DispatcherThread();
                dispatcher = scopedDispatcher;
                Assert.That(scopedDispatcher.IsForeground, Is.True);
                Assert.That(scopedDispatcher.IsAlive, Is.True);
                throw injectedFailure;
            });

            Assert.That(error, Is.SameAs(injectedFailure));
            Assert.That(dispatcher, Is.Not.Null);
            Assert.That(dispatcher.IsAlive, Is.False);
            Assert.DoesNotThrow(dispatcher.Dispose);
            Assert.That(dispatcher.IsAlive, Is.False);
        }
        finally {
            if (dispatcher is not null) {
                dispatcher.Dispose();
            }
        }
    }
}