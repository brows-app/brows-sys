using Brows.Sys;
using Brows.Sys.Messages.DeviceMessages;
using Brows.Sys.Messages.DeviceMessages.DeviceChanges;
using Brows.Sys.Messages.DeviceMessages.Devices;
using Brows.Win32.PlatformInvoke;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Brows.Win32;

internal static class Win32Message {
    private const int DeviceChangeHeaderSize = 12;
    private const int DeviceTypeOffset = 4;
    private const int VolumeRecordSize = 20;
    private const int VolumeUnitMaskOffset = 12;
    private const int VolumeFlagsOffset = 16;

    private static readonly IReadOnlyList<ISystemMessage> NoMessages = Array.Empty<ISystemMessage>();

    private static bool IsSupportedDeviceChange(DBT deviceChange) {
        var isSupportedDeviceChange = deviceChange is
            DBT.DEVICEARRIVAL or
            DBT.DEVICEQUERYREMOVE or
            DBT.DEVICEQUERYREMOVEFAILED or
            DBT.DEVICEREMOVEPENDING or
            DBT.DEVICEREMOVECOMPLETE;
        return isSupportedDeviceChange;
    }

    private static bool IsSupportedDeviceType(DBT_DEVTYP deviceType) {
        var isSupportedDeviceType = deviceType is
            DBT_DEVTYP.DEVICEINTERFACE or
            DBT_DEVTYP.HANDLE or
            DBT_DEVTYP.OEM or
            DBT_DEVTYP.PORT or
            DBT_DEVTYP.VOLUME;
        return isSupportedDeviceType;
    }

    private static bool TryReadHeader(nint lParam, out int declaredSize, out DBT_DEVTYP deviceType) {
        declaredSize = 0;
        deviceType = default;
        if (lParam == 0) {
            return false;
        }
        declaredSize = Marshal.ReadInt32(lParam);
        var headerIsTruncated = declaredSize < DeviceChangeHeaderSize;
        if (headerIsTruncated) {
            return false;
        }
        deviceType = (DBT_DEVTYP)unchecked((uint)Marshal.ReadInt32(lParam, DeviceTypeOffset));
        return true;
    }

    private static bool TryReadName(nint pointer, int declaredSize, int offset, out string name) {
        name = null;
        var tailSize = declaredSize - offset;
        var tailIsInvalid = tailSize < 2 || (tailSize & 1) != 0;
        if (tailIsInvalid) {
            return false;
        }
        for (var position = offset; position <= declaredSize - 2; position += 2) {
            if (Marshal.ReadInt16(pointer, position) == 0) {
                name = Marshal.PtrToStringUni(pointer + offset, (position - offset) / 2);
                return true;
            }
        }
        return false;
    }

    private static VolumeFlag ToVolumeFlags(DBTF nativeFlags) {
        var volumeFlags = VolumeFlag.None;
        var mediaFlagIsSet = (nativeFlags & DBTF.MEDIA) != 0;
        if (mediaFlagIsSet) {
            volumeFlags |= VolumeFlag.Media;
        }
        var networkFlagIsSet = (nativeFlags & DBTF.NET) != 0;
        if (networkFlagIsSet) {
            volumeFlags |= VolumeFlag.Network;
        }
        return volumeFlags;
    }

    private static DeviceChange CreateDeviceChange(DBT deviceChange, Device device) {
        switch (deviceChange) {
            case DBT.DEVICEARRIVAL:
                return new DeviceArrival { Device = device };
            case DBT.DEVICEQUERYREMOVE:
                return new DeviceRemovalRequest { Device = device };
            case DBT.DEVICEQUERYREMOVEFAILED:
                return new DeviceRemovalFailed { Device = device };
            case DBT.DEVICEREMOVEPENDING:
                return new DeviceRemovalPending { Device = device };
            case DBT.DEVICEREMOVECOMPLETE:
                return new DeviceRemovalComplete { Device = device };
            default:
                return null;
        }
    }

    public static IReadOnlyList<ISystemMessage> Interpret(int msg, nint wParam, nint lParam) {
        var isDeviceChangeMessage = (WM)msg == WM.DEVICECHANGE;
        if (!isDeviceChangeMessage) {
            return NoMessages;
        }
        var deviceChange = (DBT)wParam;
        if (deviceChange == DBT.DEVNODES_CHANGED) {
            return new ISystemMessage[] { new DeviceTreeChange() };
        }
        var isSupportedDeviceChange = IsSupportedDeviceChange(deviceChange);
        if (!isSupportedDeviceChange) {
            return NoMessages;
        }
        var headerIsValid = TryReadHeader(lParam, out var declaredSize, out var deviceType);
        if (!headerIsValid) {
            return NoMessages;
        }
        var isSupportedDeviceType = IsSupportedDeviceType(deviceType);
        if (!isSupportedDeviceType) {
            return NoMessages;
        }
        if (deviceType == DBT_DEVTYP.PORT) {
            if (!TryReadName(lParam, declaredSize, 12, out var portName)) {
                return NoMessages;
            }
            return new ISystemMessage[] {
                CreateDeviceChange(deviceChange, new PortDevice { PortName = portName })
            };
        }
        if (deviceType == DBT_DEVTYP.DEVICEINTERFACE) {
            if (!TryReadName(lParam, declaredSize, 28, out var interfaceName)) {
                return NoMessages;
            }
            var deviceInterface = new InterfaceDevice {
                InterfaceClassGuid = Marshal.PtrToStructure<Guid>(lParam + 12),
                InterfaceName = interfaceName,
            };
            return new ISystemMessage[] { CreateDeviceChange(deviceChange, deviceInterface) };
        }
        var isVolumePayload = deviceType == DBT_DEVTYP.VOLUME;
        if (!isVolumePayload) {
            return new ISystemMessage[] { CreateDeviceChange(deviceChange, null) };
        }
        var volumePayloadIsTruncated = declaredSize < VolumeRecordSize;
        if (volumePayloadIsTruncated) {
            return NoMessages;
        }
        var unitMask = unchecked((uint)Marshal.ReadInt32(lParam, VolumeUnitMaskOffset));
        var nativeFlags = (DBTF)unchecked((ushort)Marshal.ReadInt16(lParam, VolumeFlagsOffset));
        var volumeFlags = ToVolumeFlags(nativeFlags);
        var messages = new List<ISystemMessage>();
        for (var driveIndex = 0; driveIndex < 26; driveIndex++) {
            var driveMask = 1u << driveIndex;
            var driveIsSet = (unitMask & driveMask) != 0;
            if (!driveIsSet) {
                continue;
            }
            var volume = new VolumeDevice {
                VolumeFlag = volumeFlags,
                VolumeName = ((char)('A' + driveIndex)).ToString(),
            };
            messages.Add(CreateDeviceChange(deviceChange, volume));
        }
        return messages;
    }
}
