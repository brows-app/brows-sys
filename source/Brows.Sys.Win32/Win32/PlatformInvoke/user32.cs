using System.Runtime.InteropServices;

namespace Brows.Win32.PlatformInvoke;

internal static class user32 {
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern SafeDeviceNotificationHandle RegisterDeviceNotificationW(
        nint recipient, ref DEV_BROADCAST_DEVICEINTERFACE_W filter, DEVICE_NOTIFY flags);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterDeviceNotification(nint notification);
}
