using Brows.Win32.PlatformInvoke;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Brows.Sys;

internal sealed class Win32DeviceNotificationRegistrar : IDeviceNotificationRegistrar {
    public IDisposable Register(nint windowHandle, Guid interfaceClassGuid) {
        var filter = new DEV_BROADCAST_DEVICEINTERFACE_W {
            dbcc_size = (uint)Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE_W>(),
            dbcc_devicetype = DBT_DEVTYP.DEVICEINTERFACE,
            dbcc_classguid = interfaceClassGuid,
        };
        var registration = user32.RegisterDeviceNotificationW(
            windowHandle, ref filter, DEVICE_NOTIFY.WINDOW_HANDLE);
        var nativeError = Marshal.GetLastPInvokeError();
        if (registration.IsInvalid) {
            registration.Dispose();
            throw new Win32Exception(nativeError, "Could not register device-interface notifications.");
        }
        return registration;
    }
}
