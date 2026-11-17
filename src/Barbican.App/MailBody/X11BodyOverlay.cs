#if !WINDOWS
// Mirrors: Rust mailview.rs freeze :437 (capture of the body shown in the view's place; tag rust-final); docs/adr/0005 decision 3 and the S2
// probe "capture stand-in (XGetImage -> Image, WebView collapsed) + MenuFlyout: pass" (spikes/BodyShell/Capture/X11Capture.cs). Owner body.
//
// Uno 6.7 on X11 hides the native WebKitGTK window while a full-window overlay is up, so the body area would show the empty tile under a
// dialog. Before the overlay shows, the body's rectangle is read from the X root window (what the display shows, WebKit window included),
// put into an Image in the WebView's place, and the WebView is collapsed; Uncover restores it. Verified only by the spike so far (the VM was
// unreachable in M1): a Linux hand test.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Barbican.App.MailBody;

/// <summary>Linux (X11): a captured stand-in for the body while a dialog or scrim covers it.</summary>
public sealed partial class X11BodyOverlay(BodyShell shell) : IBodyOverlayStrategy
{
    private const int ZPixmap = 2;
    private Image? _standIn;

    public void Cover(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var view = shell.View;
        if (_standIn is not null || view.Parent is not Panel host || view.XamlRoot is null || view.ActualWidth < 1 || view.ActualHeight < 1)
        {
            return;
        }

        var scale = view.XamlRoot.RasterizationScale;
        var at = view.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        var origin = window.AppWindow.Position;
        var x = origin.X + (int)Math.Round(at.X * scale);
        var y = origin.Y + (int)Math.Round(at.Y * scale);
        var w = (int)Math.Round(view.ActualWidth * scale);
        var h = (int)Math.Round(view.ActualHeight * scale);
        var pixels = Grab(x, y, w, h);
        if (pixels is null)
        {
            return;
        }

        var bitmap = new WriteableBitmap(w, h);
        pixels.AsBuffer().CopyTo(bitmap.PixelBuffer);

        bitmap.Invalidate();
        _standIn = new Image { Source = bitmap, Stretch = Stretch.Fill, IsHitTestVisible = false };
        host.Children.Insert(host.Children.IndexOf(view) + 1, _standIn);
        view.Visibility = Visibility.Collapsed;
    }

    public void Uncover()
    {
        if (_standIn is null)
        {
            return;
        }

        shell.View.Visibility = Visibility.Visible;
        if (_standIn.Parent is Panel host)
        {
            host.Children.Remove(_standIn);
        }

        _standIn = null;
    }

    // BGRA rows of the root window's rectangle (32 bpp ZPixmap; WriteableBitmap takes premultiplied BGRA, the capture is opaque)
    private static byte[]? Grab(int x, int y, int width, int height)
    {
        var display = XOpenDisplay(0);
        if (display == 0)
        {
            return null;
        }

        try
        {
            var root = XDefaultRootWindow(display);
            var img = XGetImage(display, root, x, y, (uint)width, (uint)height, ulong.MaxValue, ZPixmap);
            if (img == 0)
            {
                return null;
            }

            try
            {
                // struct XImage (LP64): char* data at 16, bytes_per_line at 44, bits_per_pixel at 48
                var data = Marshal.ReadIntPtr(img, 16);
                var stride = Marshal.ReadInt32(img, 44);
                if (Marshal.ReadInt32(img, 48) != 32)
                {
                    return null;
                }

                var output = new byte[width * height * 4];
                for (var row = 0; row < height; row++)
                {
                    Marshal.Copy(data + (row * stride), output, row * width * 4, width * 4);
                }

                for (var i = 3; i < output.Length; i += 4)
                {
                    output[i] = 255;
                }

                return output;
            }
            finally
            {
                _ = XDestroyImage(img);
            }
        }
        finally
        {
            _ = XCloseDisplay(display);
        }
    }

    [LibraryImport("libX11.so.6")]
    private static partial nint XOpenDisplay(nint name);

    [LibraryImport("libX11.so.6")]
    private static partial nuint XDefaultRootWindow(nint display);

    [LibraryImport("libX11.so.6")]
    private static partial nint XGetImage(nint display, nuint drawable, int x, int y, uint width, uint height, ulong planeMask, int format);

    // XDestroyImage is a macro over the image's own destroy function; XFree of the data and the struct is what it does for a ZPixmap
    private static int XDestroyImage(nint img)
    {
        _ = XFree(Marshal.ReadIntPtr(img, 16));
        return XFree(img);
    }

    [LibraryImport("libX11.so.6")]
    private static partial int XFree(nint data);

    [LibraryImport("libX11.so.6")]
    private static partial int XCloseDisplay(nint display);
}
#endif
