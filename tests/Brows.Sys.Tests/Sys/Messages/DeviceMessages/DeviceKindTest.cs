using Brows.Sys.Messages.DeviceMessages;

namespace Brows.Sys.Tests;

[TestFixture]
internal sealed class DeviceKindTest {
    [Test]
    public void KeepsExistingValuesWhileAppendingNewKinds() {
        Assert.That((int)DeviceKind.None, Is.EqualTo(0));
        Assert.That((int)DeviceKind.Volume, Is.EqualTo(1));
        Assert.That((int)DeviceKind.Port, Is.EqualTo(2));
        Assert.That((int)DeviceKind.Interface, Is.EqualTo(3));
    }
}
