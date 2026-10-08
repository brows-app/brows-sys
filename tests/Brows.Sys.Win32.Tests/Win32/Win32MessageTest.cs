using Brows.Sys.Messages.DeviceMessages;
using Brows.Sys.Messages.DeviceMessages.DeviceChanges;
using Brows.Sys.Messages.DeviceMessages.Devices;
using Brows.Win32.PlatformInvoke;
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Brows.Win32;

[TestFixture]
[SupportedOSPlatform("windows")]
public sealed class Win32MessageTest {
    private const int DeviceChangeMessage = 0x0219;

    private sealed class NativeVolumePayload : IDisposable {
        public nint Pointer { get; }

        public NativeVolumePayload(uint unitMask, DBTF flags, int declaredSize = 20, uint deviceType = 2) {
            Pointer = Marshal.AllocHGlobal(20);
            Marshal.WriteInt32(Pointer, 0, declaredSize);
            Marshal.WriteInt32(Pointer, 4, unchecked((int)deviceType));
            Marshal.WriteInt32(Pointer, 8, 0);
            Marshal.WriteInt32(Pointer, 12, unchecked((int)unitMask));
            Marshal.WriteInt16(Pointer, 16, (short)flags);
            Marshal.WriteInt16(Pointer, 18, 0);
        }

        public void Dispose() {
            Marshal.FreeHGlobal(Pointer);
        }
    }

    private static DeviceChange InterpretSingle(int eventCode, nint payload) {
        var messages = Win32Message.Interpret(DeviceChangeMessage, (nint)eventCode, payload);
        Assert.That(messages, Has.Count.EqualTo(1));
        return (DeviceChange)messages[0];
    }

    private static VolumeDevice GetVolume(DeviceChange message) {
        return (VolumeDevice)message.Device;
    }

    private sealed class NativeNamePayload : IDisposable {
        internal nint Pointer { get; }
        internal NativeNamePayload(uint type, string name, int offset, Guid? classGuid = null, bool terminated = true) {
            var bytes = Encoding.Unicode.GetBytes(terminated ? name + "\0" : name);
            Pointer = Marshal.AllocHGlobal(offset + bytes.Length);
            Marshal.WriteInt32(Pointer, 0, offset + bytes.Length);
            Marshal.WriteInt32(Pointer, 4, (int)type);
            Marshal.WriteInt32(Pointer, 8, 0);
            if (offset == 28) {
                Marshal.Copy((classGuid ?? Guid.Empty).ToByteArray(), 0, Pointer + 12, 16);
            }
            Marshal.Copy(bytes, 0, Pointer + offset, bytes.Length);
        }
        public void Dispose() {
            Marshal.FreeHGlobal(Pointer);
        }
    }

    [TestCase(0x8000, DeviceChangeKind.Arrival, typeof(DeviceArrival))]
    [TestCase(0x8001, DeviceChangeKind.RemovalRequest, typeof(DeviceRemovalRequest))]
    [TestCase(0x8002, DeviceChangeKind.RemovalFailed, typeof(DeviceRemovalFailed))]
    [TestCase(0x8003, DeviceChangeKind.RemovalPending, typeof(DeviceRemovalPending))]
    [TestCase(0x8004, DeviceChangeKind.RemovalComplete, typeof(DeviceRemovalComplete))]
    public void Interpret_MapsDeviceVolumeEvents(
        int eventCode,
        DeviceChangeKind expectedChangeKind,
        Type expectedMessageType) {
        using var payload = new NativeVolumePayload(1, DBTF.MEDIA);

        var message = InterpretSingle(eventCode, payload.Pointer);

        Assert.That(message, Is.TypeOf(expectedMessageType));
        Assert.That(message.DeviceChangeKind, Is.EqualTo(expectedChangeKind));
        Assert.That(GetVolume(message).VolumeName, Is.EqualTo("A"));
    }

    [Test]
    public void Interpret_ReportsEverySupportedDriveInTheUnitMask() {
        using var payload = new NativeVolumePayload(uint.MaxValue, DBTF.MEDIA);

        var messages = Win32Message.Interpret(DeviceChangeMessage, (nint)0x8000, payload.Pointer);

        Assert.That(messages, Has.Count.EqualTo(26));
        for (var index = 0; index < messages.Count; index++) {
            var message = (DeviceChange)messages[index];
            var expectedDrive = ((char)('A' + index)).ToString();
            Assert.That(GetVolume(message).VolumeName, Is.EqualTo(expectedDrive));
        }
    }

    [TestCase(0, VolumeFlag.None)]
    [TestCase(1, VolumeFlag.Media)]
    [TestCase(2, VolumeFlag.Network)]
    [TestCase(3, VolumeFlag.Media | VolumeFlag.Network)]
    public void Interpret_PreservesIndependentVolumeFlags(int nativeFlags, VolumeFlag expectedFlags) {
        using var payload = new NativeVolumePayload(1, (DBTF)nativeFlags);

        var message = InterpretSingle(0x8000, payload.Pointer);

        Assert.That(GetVolume(message).VolumeFlag, Is.EqualTo(expectedFlags));
    }

    [Test]
    public void VolumeFlag_UsesExplicitFlagValues() {
        Assert.That(typeof(VolumeFlag).GetCustomAttribute<FlagsAttribute>(), Is.Not.Null);
        Assert.That((int)VolumeFlag.None, Is.EqualTo(0));
        Assert.That((int)VolumeFlag.Media, Is.EqualTo(1));
        Assert.That((int)VolumeFlag.Network, Is.EqualTo(2));
    }

    [Test]
    public void DbTf_UsesExplicitFlagValues() {
        Assert.That(typeof(DBTF).GetCustomAttribute<FlagsAttribute>(), Is.Not.Null);
        Assert.That((int)DBTF.MEDIA, Is.EqualTo(1));
        Assert.That((int)DBTF.NET, Is.EqualTo(2));
    }

    [Test]
    public void Interpret_IgnoresNullPayloads() {
        var messages = Win32Message.Interpret(DeviceChangeMessage, (nint)0x8000, 0);

        Assert.That(messages, Is.Empty);
    }

    [TestCase(-1)]
    [TestCase(11)]
    [TestCase(12)]
    [TestCase(19)]
    public void Interpret_IgnoresTruncatedVolumePayloads(int declaredSize) {
        using var payload = new NativeVolumePayload(1, DBTF.MEDIA, declaredSize);

        var messages = Win32Message.Interpret(DeviceChangeMessage, (nint)0x8000, payload.Pointer);

        Assert.That(messages, Is.Empty);
    }

    [TestCase((uint)DBT_DEVTYP.OEM)]
    [TestCase((uint)DBT_DEVTYP.HANDLE)]
    public void Interpret_PreservesUnmodeledDeviceTypeEvents(uint deviceType) {
        using var payload = new NativeVolumePayload(1, DBTF.MEDIA, deviceType: deviceType);

        var messages = Win32Message.Interpret(DeviceChangeMessage, (nint)0x8000, payload.Pointer);

        Assert.That(messages, Has.Count.EqualTo(1));
        Assert.That(((DeviceChange)messages[0]).Device, Is.Null);
    }

    [TestCase(7u)]
    [TestCase(0x1234u)]
    [TestCase(0xDEADBEEFu)]
    [TestCase(0xFFFFFFFFu)]
    public void Interpret_IgnoresUndefinedDeviceTypes(uint deviceType) {
        using var payload = new NativeVolumePayload(1, DBTF.MEDIA, deviceType: deviceType);

        var messages = Win32Message.Interpret(DeviceChangeMessage, (nint)0x8000, payload.Pointer);

        Assert.That(messages, Is.Empty);
    }

    [Test]
    public void Interpret_IgnoresUnrelatedMessagesWithoutReadingPayload() {
        var messages = Win32Message.Interpret(0, (nint)0x8000, 0);

        Assert.That(messages, Is.Empty);
    }


    [Test]
    public void Interpret_ReportsDeviceTreeChangesWithoutPayload() {
        var messages = Win32Message.Interpret(DeviceChangeMessage, (nint)7, 0);
        Assert.That(messages, Has.Count.EqualTo(1));
        Assert.That(messages[0].GetType().Name, Is.EqualTo("DeviceTreeChange"));
    }

    [TestCase(3u, 12, "COM3", "PortDevice")]
    [TestCase(5u, 28, @"\\?\portable#device", "InterfaceDevice")]
    public void Interpret_CopiesNamedDevicePayloads(uint type, int offset, string name, string expectedType) {
        using var payload = new NativeNamePayload(type, name, offset);
        var message = InterpretSingle(0x8000, payload.Pointer);
        Assert.That(message.Device, Is.Not.Null);
        Assert.That(message.Device.GetType().Name, Is.EqualTo(expectedType));
    }

    [TestCase(0x8000, DeviceChangeKind.Arrival)]
    [TestCase(0x8001, DeviceChangeKind.RemovalRequest)]
    [TestCase(0x8002, DeviceChangeKind.RemovalFailed)]
    [TestCase(0x8003, DeviceChangeKind.RemovalPending)]
    [TestCase(0x8004, DeviceChangeKind.RemovalComplete)]
    public void Interpret_MapsNamedDevicesForEveryChange(int code, DeviceChangeKind kind) {
        var classGuid = new Guid("6AC27878-A6FA-4155-BA85-F98F491D4F33");
        DeviceChange portChange;
        DeviceChange interfaceChange;
        using (var payload = new NativeNamePayload(3, "Pört 连接😀", 12)) {
            portChange = InterpretSingle(code, payload.Pointer);
        }
        using (var payload = new NativeNamePayload(5, @"\\?\device#连接😀", 28, classGuid)) {
            interfaceChange = InterpretSingle(code, payload.Pointer);
        }
        Assert.That(portChange.DeviceChangeKind, Is.EqualTo(kind));
        Assert.That(((PortDevice)portChange.Device).PortName, Is.EqualTo("Pört 连接😀"));
        Assert.That(interfaceChange.DeviceChangeKind, Is.EqualTo(kind));
        var deviceInterface = (InterfaceDevice)interfaceChange.Device;
        Assert.That(deviceInterface.InterfaceClassGuid, Is.EqualTo(classGuid));
        Assert.That(deviceInterface.InterfaceName, Is.EqualTo(@"\\?\device#连接😀"));
    }

    [TestCase(3u, 12, 11)]
    [TestCase(3u, 12, 12)]
    [TestCase(3u, 12, 13)]
    [TestCase(3u, 12, 15)]
    [TestCase(5u, 28, 12)]
    [TestCase(5u, 28, 27)]
    [TestCase(5u, 28, 28)]
    [TestCase(5u, 28, 29)]
    public void Interpret_RejectsShortOrOddNamedPayloads(uint type, int offset, int size) {
        using var payload = new NativeNamePayload(type, "device", offset);
        Marshal.WriteInt32(payload.Pointer, size);
        Assert.That(Win32Message.Interpret(DeviceChangeMessage, (nint)0x8000, payload.Pointer), Is.Empty);
    }

    [TestCase(3u, 12)]
    [TestCase(5u, 28)]
    public void Interpret_RequiresTerminatorWithinDeclaredSize(uint type, int offset) {
        using var payload = new NativeNamePayload(type, "device", offset, terminated: false);
        Assert.That(Win32Message.Interpret(DeviceChangeMessage, (nint)0x8000, payload.Pointer), Is.Empty);
    }

    [Test]
    public void Interpret_TreeChangeIgnoresPointerAndHasNoIndividualDevice() {
        var message = Win32Message.Interpret(DeviceChangeMessage, (nint)7, (nint)1).Single();
        Assert.That(message, Is.TypeOf<DeviceTreeChange>());
        Assert.That(((DeviceTreeChange)message).Device, Is.Null);
        Assert.That(Win32Message.Interpret(0, (nint)7, (nint)1), Is.Empty);
    }

    [Test]
    public void InterfaceRegistrationFilterMatchesNativeLayout() {
        Assert.That(Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE_W>(), Is.EqualTo(32));
        Assert.That(Marshal.OffsetOf<DEV_BROADCAST_DEVICEINTERFACE_W>("dbcc_classguid").ToInt32(), Is.EqualTo(12));
        Assert.That(Marshal.OffsetOf<DEV_BROADCAST_DEVICEINTERFACE_W>("dbcc_name").ToInt32(), Is.EqualTo(28));
    }
}
