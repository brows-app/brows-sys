using Brows.Sys.Messages.DeviceMessages.DeviceChanges;
using System;

namespace Brows;

[TestFixture]
internal sealed class EventLogTest {
    [Test]
    public void HistoryRetainsOnlyTheLatestThousandEntries() {
        var log = new EventLog();
        log.Listening();
        var timestamp = DateTimeOffset.Now;
        for (var index = 0; index < 1001; index++) {
            log.Append(new DeviceArrival(), timestamp.AddMilliseconds(index));
        }

        Assert.That(log.Count, Is.EqualTo(1000));
        Assert.That(log.Entries[0].Timestamp, Is.EqualTo(timestamp.AddMilliseconds(1).ToLocalTime()));
        Assert.That(log.Entries[999].Timestamp, Is.EqualTo(timestamp.AddMilliseconds(1000).ToLocalTime()));
    }

    [Test]
    public void PauseDiscardsNewEventsAndResumeDoesNotReplayThem() {
        var log = new EventLog();
        log.Listening();
        log.Append(new DeviceArrival(), DateTimeOffset.Now);
        log.TogglePause();
        log.Append(new DeviceRemovalComplete(), DateTimeOffset.Now);

        Assert.That(log.Status, Is.EqualTo("Paused"));
        Assert.That(log.PauseText, Is.EqualTo("Resume"));
        Assert.That(log.Count, Is.EqualTo(1));

        log.TogglePause();
        log.Append(new DeviceRemovalPending(), DateTimeOffset.Now);

        Assert.That(log.Status, Is.EqualTo("Listening"));
        Assert.That(log.Count, Is.EqualTo(2));
        Assert.That(log.Entries[1].ChangeKind, Is.EqualTo("RemovalPending"));
    }

    [Test]
    public void ClearPreservesCaptureState() {
        var log = new EventLog();
        log.Listening();
        log.Append(new DeviceArrival(), DateTimeOffset.Now);
        log.Clear();

        Assert.That(log.Entries, Is.Empty);
        Assert.That(log.Status, Is.EqualTo("Listening"));
        Assert.That(log.IsEmpty, Is.True);
        log.Append(new DeviceArrival(), DateTimeOffset.Now);
        Assert.That(log.Count, Is.EqualTo(1));
    }

    [Test]
    public void ErrorPreservesHistoryAndDisablesCapture() {
        var log = new EventLog();
        log.Listening();
        log.Append(new DeviceArrival(), DateTimeOffset.Now);
        log.Fail(new InvalidOperationException("Listener failed."));
        log.TogglePause();
        log.Append(new DeviceArrival(), DateTimeOffset.Now);

        Assert.That(log.Status, Is.EqualTo("Error"));
        Assert.That(log.Error, Is.EqualTo("Listener failed."));
        Assert.That(log.CanPause, Is.False);
        Assert.That(log.Count, Is.EqualTo(1));
    }

    [Test]
    public void AutoScrollIsEnabledByDefaultAndNotifiesChanges() {
        var log = new EventLog();
        var notified = false;
        log.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(EventLog.AutoScroll)) {
                notified = true;
            }
        };

        Assert.That(log.AutoScroll, Is.True);
        log.AutoScroll = false;
        Assert.That(notified, Is.True);
        Assert.That(log.AutoScroll, Is.False);
    }

    [Test]
    public void StoppingAndStoppedRejectUpdates() {
        var log = new EventLog();
        log.Listening();
        log.Stopping();
        log.Append(new DeviceArrival(), DateTimeOffset.Now);
        log.Stopped();
        log.Append(new DeviceArrival(), DateTimeOffset.Now);

        Assert.That(log.Count, Is.Zero);
        Assert.That(log.Status, Is.EqualTo("Stopped"));
        Assert.That(log.CanPause, Is.False);
    }
}
