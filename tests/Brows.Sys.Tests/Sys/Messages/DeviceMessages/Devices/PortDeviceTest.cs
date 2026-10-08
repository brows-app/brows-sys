namespace Brows.Sys.Messages.DeviceMessages.Devices;

[TestFixture]
internal sealed class PortDeviceTest {
    [Test]
    public void IdentifiesPortAndRetainsBroadcastName() {
        var port = new PortDevice { PortName = "COM3" };
        Assert.That(port.DeviceKind, Is.EqualTo(DeviceKind.Port));
        Assert.That(port.PortName, Is.EqualTo("COM3"));
    }
}
