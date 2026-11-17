#if WINDOWS
using System.Runtime.InteropServices;

namespace Barbican.Automation.Windows;

/// <summary>
/// Read-only Win32 queries of the automation build: DPI, client-area geometry for cropping captures,
/// and the foreground window and cursor position for the no-focus proof (<c>focus-guard</c>). Nothing
/// here moves, activates or sends input to any window.
/// </summary>
internal static partial class Native
{
    private const int DwmwaExtendedFrameBounds = 9;

    public static double Scale(nint hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1 : dpi / 96.0;
    }

    /// <summary>The client area inside the DWM frame (what WGC captures), in physical px.</summary>
    public static (int X, int Y, int Width, int Height) ClientInFrame(nint hwnd)
    {
        _ = GetClientRect(hwnd, out var c);
        var p = default(Point);
        _ = ClientToScreen(hwnd, ref p);
        _ = DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out var f, Marshal.SizeOf<Rect>());
        return (p.X - f.Left, p.Y - f.Top, c.Right - c.Left, c.Bottom - c.Top);
    }

    /// <summary>The screen position of the client origin, in physical px.</summary>
    public static (int X, int Y) ClientOrigin(nint hwnd)
    {
        var p = default(Point);
        _ = ClientToScreen(hwnd, ref p);
        return (p.X, p.Y);
    }

    /// <summary>The foreground window, the App's own window and whether they are the same, and the cursor.</summary>
    public static string FocusReport(nint own)
    {
        _ = GetCursorPos(out var cursor);
        var fg = GetForegroundWindow();
        return $"fg={fg} own={own} own-is-foreground={(fg == own ? "yes" : "no")} cursor={cursor.X},{cursor.Y}";
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hwnd, ref Point point);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
#endif
