namespace Brows.Sys.Messages.DeviceMessages.Devices;

[TestFixture]
internal sealed class InterfaceDeviceTest {
    [Test]
    public void IdentifiesInterfaceAndRetainsBroadcastIdentity() {
        var classGuid = Guid.NewGuid();
        var device = new InterfaceDevice { InterfaceClassGuid = classGuid, InterfaceName = "native-interface" };
        Assert.That(device.DeviceKind, Is.EqualTo(DeviceKind.Interface));
        Assert.That(device.InterfaceClassGuid, Is.EqualTo(classGuid));
        Assert.That(device.InterfaceName, Is.EqualTo("native-interface"));
    }
}
