using System;

using WORD = System.UInt16;

namespace Brows.Win32.PlatformInvoke;

[Flags]
internal enum DBTF : WORD {
    MEDIA = 0x0001,
    NET = 0x0002
}
