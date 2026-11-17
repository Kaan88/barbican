using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace S4;

/// <summary>Win32 pieces: an off-screen, non-activating, taskbar-less window; client-area geometry for capture.</summary>
internal static class Native
{
    internal const int GWL_EXSTYLE = -20;
    internal const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
    internal const int SW_SHOWNOACTIVATE = 4;
    internal const uint SWP_NOACTIVATE = 0x10, SWP_NOZORDER = 4, SWP_SHOWWINDOW = 0x40;
    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint h, int i, nint v);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint h, nint after, int x, int y, int w, int cx, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint h, out RECT r);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint h, ref POINT p);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT p);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint h, int attr, out RECT r, int size);
    [DllImport("user32.dll")] internal static extern bool PrintWindow(nint h, nint hdc, uint flags);
    [DllImport("user32.dll")] internal static extern nint GetDC(nint h);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint h, nint dc);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleBitmap(nint dc, int w, int h);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint o);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint o);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern int GetDIBits(nint dc, nint bmp, uint start, uint lines, byte[] bits, ref BITMAPINFO bi, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPels, biYPels, biClrUsed, biClrImportant;
    }

    internal static nint Hwnd(Window w) => WindowNative.GetWindowHandle(w);
    internal static double Scale(nint hwnd) => GetDpiForWindow(hwnd) / 96.0;

    /// <summary>Client area of the window relative to its DWM frame (what WGC captures), in physical px.</summary>
    internal static (int x, int y, int w, int h) ClientInFrame(nint hwnd)
    {
        GetClientRect(hwnd, out var c);
        var p = new POINT();
        ClientToScreen(hwnd, ref p);
        DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var f, Marshal.SizeOf<RECT>());
        return (p.X - f.Left, p.Y - f.Top, c.Right - c.Left, c.Bottom - c.Top);
    }
}

/// <summary>Shows a window far off every monitor without activating it (PORT-PLAN.md section 4.4, Layer A).</summary>
internal static class Hidden
{
    public static void ShowOffScreen(Window w, double logicalW = 1100, double logicalH = 720)
    {
        var hwnd = Native.Hwnd(w);
        var ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        ex |= (nint)(Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);   // no focus, no taskbar button, not in Alt+Tab
        ex &= ~(nint)Native.WS_EX_APPWINDOW;
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, ex);
        if (w.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p) p.SetBorderAndTitleBar(false, false);
        var s = Native.Scale(hwnd);
        w.AppWindow.MoveAndResize(new RectInt32(-12000, 200, (int)(logicalW * s), (int)(logicalH * s)));
        // The first ShowWindow of a process may be overridden by STARTUPINFO (e.g. SW_HIDE from a launcher): repeat it.
        for (var i = 0; i < 2 && !Native.IsWindowVisible(hwnd); i++) Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
        Native.SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x1 | 0x2 | Native.SWP_NOACTIVATE | Native.SWP_NOZORDER | Native.SWP_SHOWWINDOW);
    }
}
