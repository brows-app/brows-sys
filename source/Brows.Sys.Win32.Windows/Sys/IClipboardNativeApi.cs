namespace Brows.Sys;

internal interface IClipboardNativeApi {
    void AddClipboardFormatListener(nint hwnd);
    void RemoveClipboardFormatListener(nint hwnd);
    uint GetClipboardSequenceNumber();
    bool IsWindow(nint hwnd);
}
