using Brows.Sys;
using Brows.Sys.Messages;
using Brows.Sys.Messages.DeviceMessages;
using Brows.Sys.Messages.DeviceMessages.DeviceChanges;
using Brows.Sys.Messages.DeviceMessages.Devices;
using System;

namespace Brows;

[TestFixture]
internal sealed class EventLogEntryTest {
    private static DeviceChange CreateChange(DeviceChangeKind kind) {
        switch (kind) {
            case DeviceChangeKind.Arrival:
                return new DeviceArrival();
            case DeviceChangeKind.RemovalRequest:
                return new DeviceRemovalRequest();
            case DeviceChangeKind.RemovalFailed:
                return new DeviceRemovalFailed();
            case DeviceChangeKind.RemovalPending:
                return new DeviceRemovalPending();
            case DeviceChangeKind.RemovalComplete:
                return new DeviceRemovalComplete();
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    [TestCase(DeviceChangeKind.Arrival)]
    [TestCase(DeviceChangeKind.RemovalRequest)]
    [TestCase(DeviceChangeKind.RemovalFailed)]
    [TestCase(DeviceChangeKind.RemovalPending)]
    [TestCase(DeviceChangeKind.RemovalComplete)]
    public void FromMessageFormatsDeviceChanges(DeviceChangeKind kind) {
        var message = CreateChange(kind) with {
            Device = new VolumeDevice { VolumeName = "E", VolumeFlag = VolumeFlag.Media | VolumeFlag.Network },
        };
        var timestamp = new DateTimeOffset(2026, 10, 7, 12, 30, 45, TimeSpan.Zero);

        var entry = EventLogEntry.FromMessage(message, timestamp);

        Assert.That(entry.Timestamp, Is.EqualTo(timestamp.ToLocalTime()));
        Assert.That(entry.Category, Is.EqualTo(SystemMessageKind.Device));
        Assert.That(entry.MessageType, Is.EqualTo(message.GetType().Name));
        Assert.That(entry.ChangeKind, Is.EqualTo(kind.ToString()));
        Assert.That(entry.ClipboardSequence, Is.EqualTo("—"));
        Assert.That(entry.DeviceKind, Is.EqualTo("Volume"));
        Assert.That(entry.DeviceName, Is.EqualTo("E"));
        Assert.That(entry.Flags, Is.EqualTo("Media, Network"));
    }

    [Test]
    public void FromMessageFormatsClipboardSequenceAsInvariantDecimal() {
        var originalCulture = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var timestamp = new DateTimeOffset(2026, 10, 7, 12, 30, 45, TimeSpan.Zero);
            var entry = EventLogEntry.FromMessage(new ClipboardChange {
                SequenceNumber = uint.MaxValue,
            }, timestamp);

            Assert.That(entry.Timestamp, Is.EqualTo(timestamp.ToLocalTime()));
            Assert.That(entry.Category, Is.EqualTo(SystemMessageKind.Clipboard));
            Assert.That(entry.MessageType, Is.EqualTo(nameof(ClipboardChange)));
            Assert.That(entry.ChangeKind, Is.EqualTo("—"));
            Assert.That(entry.ClipboardSequence, Is.EqualTo("4294967295"));
            Assert.That(entry.DeviceKind, Is.EqualTo("—"));
            Assert.That(entry.DeviceName, Is.EqualTo("—"));
            Assert.That(entry.InterfaceClass, Is.EqualTo("—"));
            Assert.That(entry.Flags, Is.EqualTo("—"));
        }
        finally {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    public void FromMessageShowsUnavailableForClipboardSequenceZero() {
        var entry = EventLogEntry.FromMessage(new ClipboardChange(), DateTimeOffset.Now);

        Assert.That(entry.ClipboardSequence, Is.EqualTo("Unavailable"));
        Assert.That(entry.Category, Is.EqualTo(SystemMessageKind.Clipboard));
        Assert.That(entry.DeviceKind, Is.EqualTo("—"));
        Assert.That(entry.DeviceName, Is.EqualTo("—"));
        Assert.That(entry.InterfaceClass, Is.EqualTo("—"));
        Assert.That(entry.Flags, Is.EqualTo("—"));
    }

    [Test]
    public void FromMessageHandlesMissingDevice() {
        var entry = EventLogEntry.FromMessage(new DeviceArrival(), DateTimeOffset.Now);

        Assert.That(entry.Category, Is.EqualTo(SystemMessageKind.Device));
        Assert.That(entry.ClipboardSequence, Is.EqualTo("—"));
        Assert.That(entry.DeviceKind, Is.EqualTo("—"));
        Assert.That(entry.DeviceName, Is.EqualTo("—"));
        Assert.That(entry.Flags, Is.EqualTo("—"));
    }

    [Test]
    public void FromMessageHandlesUnfamiliarMessages() {
        var entry = EventLogEntry.FromMessage(new UnfamiliarMessage(), DateTimeOffset.Now);

        Assert.That(entry.MessageType, Is.EqualTo(nameof(UnfamiliarMessage)));
        Assert.That(entry.ChangeKind, Is.EqualTo("—"));
        Assert.That(entry.DeviceKind, Is.EqualTo("—"));
    }

    [Test]
    public void FromMessageHandlesMissingVolumeNameAndDefaultFlags() {
        var entry = EventLogEntry.FromMessage(new DeviceArrival { Device = new VolumeDevice() }, DateTimeOffset.Now);

        Assert.That(entry.DeviceName, Is.EqualTo("—"));
        Assert.That(entry.Flags, Is.EqualTo("None"));
    }

    private sealed record UnfamiliarMessage : SystemMessage {
        public override SystemMessageKind SystemMessageKind => SystemMessageKind.Device;
    }

    [Test]
    public void FromMessageFormatsTreeChange() {
        var entry = EventLogEntry.FromMessage(new DeviceTreeChange(), DateTimeOffset.Now);
        Assert.That(entry.MessageType, Is.EqualTo("DeviceTreeChange"));
        Assert.That(entry.ChangeKind, Is.EqualTo("TreeChange"));
        Assert.That(entry.DeviceKind, Is.EqualTo("—"));
        Assert.That(entry.DeviceName, Is.EqualTo("—"));
        Assert.That(entry.InterfaceClass, Is.EqualTo("—"));
    }

    [Test]
    public void FromMessageFormatsPortAndInterfaceNames() {
        var port = EventLogEntry.FromMessage(new DeviceArrival {
            Device = new PortDevice { PortName = "COM3" }
        }, DateTimeOffset.Now);
        Assert.That(port.DeviceKind, Is.EqualTo("Port"));
        Assert.That(port.DeviceName, Is.EqualTo("COM3"));
        Assert.That(port.InterfaceClass, Is.EqualTo("—"));
        var classGuid = Guid.NewGuid();
        var deviceInterface = EventLogEntry.FromMessage(new DeviceRemovalComplete {
            Device = new InterfaceDevice { InterfaceName = @"\\?\portable#device", InterfaceClassGuid = classGuid }
        }, DateTimeOffset.Now);
        Assert.That(deviceInterface.DeviceKind, Is.EqualTo("Interface"));
        Assert.That(deviceInterface.DeviceName, Is.EqualTo(@"\\?\portable#device"));
        Assert.That(deviceInterface.InterfaceClass, Is.EqualTo(classGuid.ToString("D")));
        Assert.That(deviceInterface.Flags, Is.EqualTo("—"));
    }
}
