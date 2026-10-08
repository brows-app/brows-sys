using System;
using System.Runtime.InteropServices;

namespace Brows.Win32.PlatformInvoke;

internal partial class kernel32 {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandleW([In, Optional, MarshalAs(UnmanagedType.LPWStr)] string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(
        [In] IntPtr hDevice,
        [In] uint dwIoControlCode,
        [In, Optional] IntPtr lpInBuffer,
        [In] uint nInBufferSize,
        [Out, Optional] IntPtr lpOutBuffer,
        [In] uint nOutBufferSize,
        [Out, Optional] out uint lpBytesReturned,
        [In, Out, Optional] IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FindNextChangeNotification([In] IntPtr hChangeHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FindCloseChangeNotification([In] IntPtr hChangeHandle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ReadDirectoryChangesW(
      [In] IntPtr hDirectory,
      [Out] IntPtr lpBuffer,
      [In] uint nBufferLength,
      [In] bool bWatchSubtree,
      [In] uint dwNotifyFilter,
      [Out, Optional] out uint lpBytesReturned,
      [In, Out, Optional] IntPtr lpOverlapped,
      [In, Optional] IntPtr lpCompletionRoutine
    );
}
