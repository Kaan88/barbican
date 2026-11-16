#if !WINDOWS
using System.Runtime.InteropServices;

namespace Barbican.Spike.BodyShell.Capture;

/// <summary>
/// XGetImage of the X root window on its own display connection, polled (X11 has no "frame
/// composed" event without a compositor; under Xvfb nothing is vsync-paced). The root image is what
/// the screen shows, so the separate WebKitGTK window and the Uno window are seen stacked as the user
/// would see them: this is how the Linux airspace problem is measured.
/// </summary>
internal sealed partial class X11Capture : IFrameSource
{
    private const int ZPixmap = 2;
    private readonly nint _display;
    private readonly nuint _root;
    private readonly int _width;
    private readonly int _height;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public X11Capture()
    {
        _display = XOpenDisplay(0);
        if (_display == 0)
        {
            throw new InvalidOperationException("XOpenDisplay failed (is DISPLAY set?)");
        }

        _root = XDefaultRootWindow(_display);
        _width = XDisplayWidth(_display, 0);
        _height = XDisplayHeight(_display, 0);
    }

    public string Name => "XGetImage (root window, polled)";

    public void Start(Action<CapturedFrame> sink)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(8));
            while (!token.IsCancellationRequested)
            {
                var f = Grab();
                if (f is not null)
                {
                    sink(f);
                }

                try
                {
                    await timer.WaitForNextTickAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        if (_loop is not null)
        {
            await _loop.WaitAsync(TimeSpan.FromSeconds(5));
        }

        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    public CapturedFrame? Grab()
    {
        lock (_gate)
        {
            var t = Clock.Now;
            var img = XGetImage(_display, _root, 0, 0, (uint)_width, (uint)_height, ulong.MaxValue, ZPixmap);
            if (img == 0)
            {
                return null;
            }

            try
            {
                // struct XImage (LP64): width, height, xoffset, format (4 x int), char* data at 16,
                // byte_order 24, bitmap_unit 28, bitmap_bit_order 32, bitmap_pad 36, depth 40,
                // bytes_per_line 44, bits_per_pixel 48.
                var data = Marshal.ReadIntPtr(img, 16);
                var stride = Marshal.ReadInt32(img, 44);
                var bpp = Marshal.ReadInt32(img, 48);
                if (bpp != 32)
                {
                    throw new NotSupportedException($"X image with {bpp} bits per pixel");
                }

                var bytes = new byte[stride * _height];
                Marshal.Copy(data, bytes, 0, bytes.Length);
                _ = XFree(data);
                return new CapturedFrame(t, _width, _height, stride, bytes);
            }
            finally
            {
                _ = XFree(img);
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _ = XCloseDisplay(_display);
    }

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
