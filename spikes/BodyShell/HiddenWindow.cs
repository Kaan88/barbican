#if WINDOWS
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace Barbican.Spike.BodyShell;

/// <summary>
/// Shows a window where the user never sees it and without taking focus (PORT-PLAN.md §4.4
/// layer A): to the right of the whole virtual screen, WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW (no
/// taskbar button, no Alt+Tab entry), shown with activateWindow false. Window.Activate() is never
/// called.
/// </summary>
internal static partial class HiddenWindow
{
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExToolWindow = 0x00000080;
    private const int SmXVirtualScreen = 76;
    private const int SmCxVirtualScreen = 78;

    public static nint Handle(Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);

    public static void ShowOffScreen(Window window, int width, int height)
    {
        var hwnd = Handle(window);
        var ex = GetWindowLongPtrW(hwnd, GwlExStyle);
        _ = SetWindowLongPtrW(hwnd, GwlExStyle, (nint)((long)ex | WsExNoActivate | WsExToolWindow));
        var app = window.AppWindow;
        app.IsShownInSwitchers = false;
        var x = GetSystemMetrics(SmXVirtualScreen) + GetSystemMetrics(SmCxVirtualScreen) + 400;
        app.MoveAndResize(new RectInt32(x, 0, width, height));
        app.Show(false);
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);
}
#endif
