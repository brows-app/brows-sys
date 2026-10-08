using Brows.Sys.Messages;
using Brows.Sys.Messages.DeviceMessages;

namespace Brows.Sys.Tests;

[TestFixture]
internal sealed class DeviceTreeChangeTest {
    [Test]
    public void DescribesInventoryChangeWithoutIndividualDevice() {
        var message = new DeviceTreeChange();
        Assert.That(message.SystemMessageKind, Is.EqualTo(SystemMessageKind.Device));
        Assert.That(message.DeviceMessageKind, Is.EqualTo(DeviceMessageKind.TreeChange));
        Assert.That(message.Device, Is.Null);
    }
}
