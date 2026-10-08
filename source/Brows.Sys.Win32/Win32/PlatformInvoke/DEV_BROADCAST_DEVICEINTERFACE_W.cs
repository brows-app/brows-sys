using System.Runtime.InteropServices;

namespace Brows.Win32.PlatformInvoke;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DEV_BROADCAST_DEVICEINTERFACE_W {
    internal uint dbcc_size;
    internal DBT_DEVTYP dbcc_devicetype;
    internal uint dbcc_reserved;
    internal Guid dbcc_classguid;
    internal ushort dbcc_name;
}
