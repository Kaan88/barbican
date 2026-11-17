#if !WINDOWS
using System.Runtime.InteropServices;
using Barbican.Automation.Capture;

namespace Barbican.Automation.Linux;

/// <summary>
/// XGetImage of a rectangle of the X root window on its own display connection (port of
/// spikes/BodyShell/Capture/X11Capture.cs). The root image is what the display shows, so the separate
/// WebKitGTK window and the Uno window are seen stacked as a user would see them. Only used under the
/// VM's private Xvfb (the App refuses hidden runs anywhere else on Linux).
/// </summary>
internal sealed partial class X11Capture : IDisposable
{
    private const int ZPixmap = 2;
    private readonly Lock _gate = new();
    private readonly nint _display;
    private readonly nuint _root;

    public X11Capture()
    {
        _display = XOpenDisplay(0);
        if (_display == 0)
        {
            throw new InvalidOperationException("XOpenDisplay failed (is DISPLAY set?)");
        }

        _root = XDefaultRootWindow(_display);
        ScreenWidth = XDisplayWidth(_display, 0);
        ScreenHeight = XDisplayHeight(_display, 0);
    }

    public int ScreenWidth { get; }

    public int ScreenHeight { get; }

    /// <summary>The rectangle (clamped to the screen), or null when the server refused.</summary>
    public Shot? Grab(int x, int y, int width, int height)
    {
        x = Math.Clamp(x, 0, ScreenWidth - 1);
        y = Math.Clamp(y, 0, ScreenHeight - 1);
        width = Math.Clamp(width, 1, ScreenWidth - x);
        height = Math.Clamp(height, 1, ScreenHeight - y);
        lock (_gate)
        {
            var img = XGetImage(_display, _root, x, y, (uint)width, (uint)height, ulong.MaxValue, ZPixmap);
            if (img == 0)
            {
                return null;
            }

            try
            {
                // struct XImage (LP64): width, height, xoffset, format (4 x int), char* data at 16,
                // bytes_per_line at 44, bits_per_pixel at 48.
                var data = Marshal.ReadIntPtr(img, 16);
                var stride = Marshal.ReadInt32(img, 44);
                var bpp = Marshal.ReadInt32(img, 48);
                if (bpp != 32)
                {
                    throw new NotSupportedException($"X image with {bpp} bits per pixel");
                }

                var bytes = new byte[stride * height];
                Marshal.Copy(data, bytes, 0, bytes.Length);
                _ = XFree(data);
                return new Shot(width, height, stride, bytes);
            }
            finally
            {
                _ = XFree(img);
            }
        }
    }

    public void Dispose() => _ = XCloseDisplay(_display);

    [LibraryImport("libX11.so.6")]
    private static partial nint XOpenDisplay(nint name);

    [LibraryImport("libX11.so.6")]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport("libX11.so.6")]
    private static partial nuint XDefaultRootWindow(nint display);

    [LibraryImport("libX11.so.6")]
    private static partial int XDisplayWidth(nint display, int screen);

    [LibraryImport("libX11.so.6")]
    private static partial int XDisplayHeight(nint display, int screen);

    [LibraryImport("libX11.so.6")]
    private static partial nint XGetImage(nint display, nuint drawable, int x, int y, uint width, uint height, ulong planeMask, int format);

    [LibraryImport("libX11.so.6")]
    private static partial int XFree(nint data);
}
#endif
