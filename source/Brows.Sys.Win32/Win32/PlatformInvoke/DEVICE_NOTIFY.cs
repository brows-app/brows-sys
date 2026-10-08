using System;

using DWORD = System.UInt32;

namespace Brows.Win32.PlatformInvoke;

[Flags]
internal enum DEVICE_NOTIFY : DWORD {
    WINDOW_HANDLE = 0x00000000,
    SERVICE_HANDLE = 0x00000001,
    ALL_INTERFACE_CLASSES = 0x00000004
}
