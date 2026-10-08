using Brows.Sys.Messages;

namespace Brows.Sys.Tests;

[TestFixture]
internal sealed class DeviceMessageKindTest {
    [Test]
    public void KeepsChangeValueWhileAppendingTreeChange() {
        Assert.That((int)DeviceMessageKind.Change, Is.EqualTo(0));
        Assert.That((int)DeviceMessageKind.TreeChange, Is.EqualTo(1));
    }
}
