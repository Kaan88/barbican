using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Barbican;

/// Entry point of the capture harness: the prototype's App, but the window is shown off-screen without activation
/// (WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, no border or title bar, at (-12000, 200), SW_SHOWNOACTIVATE), the same design as
/// src/Barbican.App/Composition/HiddenWindow.cs. It refuses to show when a monitor reaches that rectangle.
public partial class App : Application
{
    public static MainWindow Main;

    public App()
    {
        var outDir = Environment.GetCommandLineArgs().Length > 1 ? Environment.GetCommandLineArgs()[1] : "proto-shots";
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(Path.GetFullPath(outDir), ".webview2"));
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var outDir = Path.GetFullPath(Environment.GetCommandLineArgs().Length > 1 ? Environment.GetCommandLineArgs()[1] : "proto-shots");
        Directory.CreateDirectory(outDir);
        Main = new MainWindow();
        Hidden.Show(Main, 1560, 960);
        _ = Main.RunCaptureAsync(outDir);
    }
}

static class Hidden
{
    const int X = -12000, Y = 200;

    public static double Scale(Window w)
    {
        var s = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(w)) / 96.0;
        return s <= 0 ? 1 : s;
    }

    public static void Show(Window w, int width, int height)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(w);
        if (IsWindowVisible(hwnd)) throw new InvalidOperationException("shown before hidden");
        var ex = (long)GetWindowLongPtrW(hwnd, -20);
        ex = (ex | 0x08000000 | 0x00000080) & ~0x00040000;
        SetWindowLongPtrW(hwnd, -20, (nint)ex);
        try { w.AppWindow.IsShownInSwitchers = false; } catch (NotImplementedException) { }
        if (w.AppWindow.Presenter is OverlappedPresenter p) p.SetBorderAndTitleBar(false, false);
        var s = Scale(w);
        var r = new Rect { L = X, T = Y, R = X + (int)(width * s), B = Y + (int)(height * s) };
        if (MonitorFromRect(r, 0) != 0) throw new InvalidOperationException("a monitor covers the off-screen position");
        w.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(X, Y, (int)(width * s), (int)(height * s)));
        if (!GetWindowRect(hwnd, out var placed) || MonitorFromRect(placed, 0) != 0) throw new InvalidOperationException("not off-screen");
        for (var i = 0; i < 2 && !IsWindowVisible(hwnd); i++) ShowWindow(hwnd, 4);
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x1 | 0x2 | 0x4 | 0x10 | 0x40);
    }

    [StructLayout(LayoutKind.Sequential)] struct Rect { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern nint GetWindowLongPtrW(nint h, int i);
    [DllImport("user32.dll")] static extern nint SetWindowLongPtrW(nint h, int i, nint v);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint h, int c);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint h, nint a, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint h, out Rect r);
    [DllImport("user32.dll")] static extern nint MonitorFromRect(in Rect r, uint f);
}
