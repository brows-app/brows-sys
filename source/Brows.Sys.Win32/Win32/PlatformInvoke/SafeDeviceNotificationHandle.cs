using Microsoft.Win32.SafeHandles;

namespace Brows.Win32.PlatformInvoke;

internal sealed class SafeDeviceNotificationHandle : SafeHandleZeroOrMinusOneIsInvalid {
    internal SafeDeviceNotificationHandle() : base(true) {
    }

    protected override bool ReleaseHandle() {
        return user32.UnregisterDeviceNotification(handle);
    }
}
