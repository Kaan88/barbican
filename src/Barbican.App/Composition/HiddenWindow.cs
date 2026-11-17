#if WINDOWS
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Windows.Graphics;
#endif

namespace Barbican.App.Composition;

/// <summary>
/// The one design of a hidden window (docs/adr/0007 decision 1; FABLE #27: one position for every
/// hidden run). Before the window is ever shown: <c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW</c> (no
/// focus, no taskbar button, not in Alt+Tab), no border or title bar, moved and sized to
/// (<see cref="OffScreenX"/>, <see cref="OffScreenY"/>), then <c>ShowWindow(SW_SHOWNOACTIVATE)</c>.
/// <c>Window.Activate()</c> is never called on this path, so the user's foreground window, focus and
/// screen are never touched. DWM keeps composing the window there (WGC captures it, WebView2 renders).
/// Only the WinUI head implements it; the Uno desktop head runs hidden only under a private Xvfb.
/// </summary>
internal static partial class HiddenWindow
{
    public const int OffScreenX = -12000;
    public const int OffScreenY = 200;

#if WINDOWS
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExAppWindow = 0x00040000;
    private const int SwShowNoActivate = 4;
    private const uint SwpNoSize = 0x1;
    private const uint SwpNoMove = 0x2;
    private const uint SwpNoZOrder = 0x4;
    private const uint SwpNoActivate = 0x10;
    private const uint SwpShowWindow = 0x40;

    public static bool IsSupported => true;

    public static nint Handle(Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);

    /// <summary>Shows <paramref name="window"/> off-screen without activating it. Call instead of Activate().</summary>
    public static void Show(Window window, int logicalWidth, int logicalHeight)
    {
        ArgumentNullException.ThrowIfNull(window);
        var hwnd = Handle(window);
        if (IsWindowVisible(hwnd))
        {
            // Visible before we placed it would mean someone showed it on screen first: refuse.
            throw new InvalidOperationException("the window was shown before it was hidden");
        }

        var ex = (long)GetWindowLongPtrW(hwnd, GwlExStyle);
        ex = (ex | WsExNoActivate | WsExToolWindow) & ~WsExAppWindow;
        _ = SetWindowLongPtrW(hwnd, GwlExStyle, (nint)ex);
        var app = window.AppWindow;
        app.IsShownInSwitchers = false;
        if (app.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
        }

        var scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0)
        {
            scale = 1;
        }

        app.MoveAndResize(new RectInt32(OffScreenX, OffScreenY, (int)(logicalWidth * scale), (int)(logicalHeight * scale)));
        // A launcher's STARTUPINFO (SW_HIDE) can override the first ShowWindow of a process: repeat it.
        for (var i = 0; i < 2 && !IsWindowVisible(hwnd); i++)
        {
            _ = ShowWindow(hwnd, SwShowNoActivate);
        }

        _ = SetWindowPos(hwnd, 0, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoZOrder | SwpShowWindow);
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);
#else
    public static bool IsSupported => false;

    public static void Show(Window window, int logicalWidth, int logicalHeight) =>
        throw new PlatformNotSupportedException("the hidden window is implemented by the WinUI head only");
#endif
}
