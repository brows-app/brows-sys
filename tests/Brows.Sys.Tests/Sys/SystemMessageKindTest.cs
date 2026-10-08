namespace Brows.Sys;

[TestFixture]
internal sealed class SystemMessageKindTest {
    [Test]
    public void PreservesCategoryValuesAndDefaultsToNone() {
        var defaultKind = default(SystemMessageKind);

        Assert.Multiple(() => {
            Assert.That((int)SystemMessageKind.None, Is.Zero);
            Assert.That((int)SystemMessageKind.Device, Is.EqualTo(1));
            Assert.That((int)SystemMessageKind.Clipboard, Is.EqualTo(2));
            Assert.That(defaultKind, Is.EqualTo(SystemMessageKind.None));
        });
    }
}
