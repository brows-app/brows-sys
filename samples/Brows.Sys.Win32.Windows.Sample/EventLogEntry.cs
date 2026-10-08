using Brows.Sys;
using Brows.Sys.Messages.DeviceMessages;
using Brows.Sys.Messages.DeviceMessages.Devices;

namespace Brows;

internal sealed record EventLogEntry(
    DateTimeOffset Timestamp,
    string MessageType,
    string ChangeKind,
    string DeviceKind,
    string DeviceName,
    string InterfaceClass,
    string Flags) {
    private const string Unavailable = "—";

    private static string Display(string value) {
        return string.IsNullOrWhiteSpace(value) ? Unavailable : value;
    }

    internal static EventLogEntry FromMessage(ISystemMessage message, DateTimeOffset timestamp) {
        if (message is null) {
            throw new ArgumentNullException(nameof(message));
        }
        var change = message as DeviceChange;
        var device = change?.Device;
        var volume = device as VolumeDevice;
        var deviceInterface = device as InterfaceDevice;
        var deviceName = device switch {
            VolumeDevice v => v.VolumeName,
            PortDevice p => p.PortName,
            InterfaceDevice i => i.InterfaceName,
            _ => null,
        };
        var changeKind = message is DeviceTreeChange ?
            "TreeChange" : change?.DeviceChangeKind.ToString() ?? Unavailable;
        return new EventLogEntry(
            timestamp.ToLocalTime(),
            message.GetType().Name,
            changeKind,
            device?.DeviceKind.ToString() ?? Unavailable,
            Display(deviceName),
            deviceInterface?.InterfaceClassGuid.ToString("D") ?? Unavailable,
            volume?.VolumeFlag.ToString() ?? Unavailable);
    }
}
