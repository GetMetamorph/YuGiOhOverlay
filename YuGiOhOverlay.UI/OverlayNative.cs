using System.Runtime.InteropServices;

namespace YuGiOhOverlay.UI;

internal static class OverlayNative
{
    private const int ExtendedStyle = -20;
    private const long Transparent = 0x20, NoActivate = 0x08000000, Layered = 0x80000;
    internal static void SetPassive(nint handle, bool passive)
    {
        var style = GetWindowLongPtr(handle, ExtendedStyle).ToInt64() | Layered;
        style = passive ? style | Transparent | NoActivate : style & ~(Transparent | NoActivate);
        SetWindowLongPtr(handle, ExtendedStyle, (nint)style);
    }
    internal static void ExcludeFromCapture(nint handle) => SetWindowDisplayAffinity(handle, 0x11);
    internal static void Position(nint handle, int x, int y, int width, int height) => SetWindowPos(handle, new nint(-1), x, y, width, height, 0x0040);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint handle, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint handle, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint handle, uint affinity);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(nint handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint handle, int id);
}
