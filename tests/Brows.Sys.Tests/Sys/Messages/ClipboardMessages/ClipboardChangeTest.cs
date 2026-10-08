namespace Brows.Sys.Messages.ClipboardMessages;

[TestFixture]
internal sealed class ClipboardChangeTest {
    [Test]
    public void IdentifiesClipboardCategory() {
        var message = new ClipboardChange();
        Assert.That(message.SystemMessageKind, Is.EqualTo(SystemMessageKind.Clipboard));
    }

    [Test]
    public void DefaultsSequenceNumberToZero() {
        var message = new ClipboardChange();
        Assert.That(message.SequenceNumber, Is.Zero);
    }

    [Test]
    public void RetainsExplicitSequenceNumbers() {
        var zeroSequence = new ClipboardChange { SequenceNumber = 0 };
        var nonzeroSequence = new ClipboardChange { SequenceNumber = 1 };
        var maximumSequence = new ClipboardChange { SequenceNumber = uint.MaxValue };

        using (Assert.EnterMultipleScope()) {
            Assert.That(zeroSequence.SequenceNumber, Is.Zero);
            Assert.That(nonzeroSequence.SequenceNumber, Is.EqualTo(1));
            Assert.That(maximumSequence.SequenceNumber, Is.EqualTo(uint.MaxValue));
        }
    }

    [Test]
    public void PreservesSequenceNumberWhenCopiedAsRecord() {
        var message = new ClipboardChange { SequenceNumber = uint.MaxValue };
        var copy = message with { };

        Assert.That(copy.SequenceNumber, Is.EqualTo(message.SequenceNumber));
    }
}
