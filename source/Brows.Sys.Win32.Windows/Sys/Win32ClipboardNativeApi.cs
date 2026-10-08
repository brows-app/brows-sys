using Brows.Win32.PlatformInvoke;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Brows.Sys;

internal sealed class Win32ClipboardNativeApi : IClipboardNativeApi {
    public void AddClipboardFormatListener(nint hwnd) {
        var succeeded = user32.AddClipboardFormatListener(hwnd);
        var nativeError = Marshal.GetLastPInvokeError();
        if (!succeeded) {
            throw new Win32Exception(nativeError, "Could not add the clipboard format listener.");
        }
    }

    public void RemoveClipboardFormatListener(nint hwnd) {
        var succeeded = user32.RemoveClipboardFormatListener(hwnd);
        var nativeError = Marshal.GetLastPInvokeError();
        if (!succeeded) {
            throw new Win32Exception(nativeError, "Could not remove the clipboard format listener.");
        }
    }

    public uint GetClipboardSequenceNumber() {
        return user32.GetClipboardSequenceNumber();
    }

    public bool IsWindow(nint hwnd) {
        return user32.IsWindow(hwnd);
    }
}
