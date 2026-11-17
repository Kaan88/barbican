#if !WINDOWS
using System.Runtime.InteropServices;

namespace Barbican.Automation.Linux;

/// <summary>The window's rectangle on the X display, in physical px (AppWindow position and size on Uno X11).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct WindowRect(int X, int Y, int Width, int Height)
{
    public static WindowRect Of(AutomationContext context)
    {
        var app = context.Window.AppWindow;
        var p = app.Position;
        var s = app.Size;
        return new WindowRect(Math.Max(0, p.X), Math.Max(0, p.Y), Math.Max(1, s.Width), Math.Max(1, s.Height));
    }
}
#endif
