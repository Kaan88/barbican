using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace S4;

/// <summary>A captured image: BGRA8, top-down, client area only (cropped), optionally downscaled by Step.</summary>
internal sealed record Shot(int W, int H, byte[] Bgra);

/// <summary>One Windows.Graphics.Capture session on the app window. Free-threaded frame pool: FrameArrived
/// runs on a pool thread, once per composed frame (nothing arrives while the window is idle).</summary>
internal sealed class WgcSession : IDisposable
{
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);
        nint CreateForMonitor(nint monitor, in Guid iid);
    }

    [DllImport("combase.dll", ExactSpelling = true)] static extern int RoGetActivationFactory(nint classId, in Guid iid, out nint factory);
    [DllImport("combase.dll", ExactSpelling = true)] static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string s, int len, out nint h);
    [DllImport("combase.dll", ExactSpelling = true)] static extern int WindowsDeleteString(nint h);

    static GraphicsCaptureItem ItemForWindow(nint hwnd)
    {
        var iid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
        WindowsCreateString("Windows.Graphics.Capture.GraphicsCaptureItem", 44, out var cls);
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(cls, iid, out var f));
            var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(f);
            var itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");   // IGraphicsCaptureItem
            var p = interop.CreateForWindow(hwnd, itemIid);
            return GraphicsCaptureItem.FromAbi(p);
        }
        finally { WindowsDeleteString(cls); }
    }

    readonly nint hwnd;
    readonly GraphicsCaptureItem item;
    readonly Direct3D11CaptureFramePool pool;
    readonly GraphicsCaptureSession session;
    readonly IDirect3DDevice device = CanvasDevice.GetSharedDevice();
    Windows.Graphics.SizeInt32 size;

    /// <summary>(frame system-relative time ms, arrival Stopwatch ms, frame). The frame is valid only inside the call.</summary>
    public event Action<double, double, Direct3D11CaptureFrame> Frame;

    public WgcSession(nint hwnd)
    {
        this.hwnd = hwnd;
        item = ItemForWindow(hwnd);
        size = new Windows.Graphics.SizeInt32 { Width = item.Size.Width, Height = item.Size.Height };
        pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, item.Size);
        pool.FrameArrived += (p, _) =>
        {
            using var f = p.TryGetNextFrame();
            if (f == null) return;
            var arrive = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
            if (f.ContentSize.Width != size.Width || f.ContentSize.Height != size.Height)
            {
                size = f.ContentSize;
                p.Recreate(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, f.ContentSize);
            }
            Frame?.Invoke(f.SystemRelativeTime.TotalMilliseconds, arrive, f);
        };
        session = pool.CreateCaptureSession(item);
        try { session.IsCursorCaptureEnabled = false; } catch { }
        try { session.IsBorderRequired = false; } catch { }
        try { session.MinUpdateInterval = TimeSpan.FromMilliseconds(1); } catch { }
        session.StartCapture();
    }

    /// <summary>Copy the frame's client area (crop), downscaled by step, to BGRA bytes. Call inside Frame.</summary>
    public Shot Read(Direct3D11CaptureFrame f, int step = 1)
    {
        using var bmp = CanvasBitmap.CreateFromDirect3D11Surface(CanvasDevice.GetSharedDevice(), f.Surface);
        var all = bmp.GetPixelBytes();
        var fw = (int)bmp.SizeInPixels.Width;
        var fh = (int)bmp.SizeInPixels.Height;
        var (cx, cy, cw, ch) = Native.ClientInFrame(hwnd);
        cx = Math.Clamp(cx, 0, fw); cy = Math.Clamp(cy, 0, fh);
        cw = Math.Min(cw, fw - cx); ch = Math.Min(ch, fh - cy);
        step = Math.Max(1, step);
        int ow = cw / step, oh = ch / step;
        var o = new byte[ow * oh * 4];
        for (var y = 0; y < oh; y++)
            for (var x = 0; x < ow; x++)
            {
                var s = ((cy + y * step) * fw + cx + x * step) * 4;
                var d = (y * ow + x) * 4;
                o[d] = all[s]; o[d + 1] = all[s + 1]; o[d + 2] = all[s + 2]; o[d + 3] = 255;
            }
        return new Shot(ow, oh, o);
    }

    public void Dispose() { session.Dispose(); pool.Dispose(); }
}

internal static class Capture
{
    /// <summary>One WGC frame of the window including WebView2 and popups: start a session, take the first frame, stop.</summary>
    public static async Task<Shot> OnceAsync(nint hwnd, int step = 1, int timeoutMs = 3000)
    {
        var tcs = new TaskCompletionSource<Shot>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var s = new WgcSession(hwnd);
        s.Frame += (_, _, f) => { if (!tcs.Task.IsCompleted) { try { tcs.TrySetResult(s.Read(f, step)); } catch (Exception e) { tcs.TrySetException(e); } } };
        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
        return done == tcs.Task ? await tcs.Task : null;
    }

    /// <summary>Fallback as in capture.rs: PrintWindow(PW_CLIENTONLY | PW_RENDERFULLCONTENT).</summary>
    public static Shot PrintWindow(nint hwnd)
    {
        Native.GetClientRect(hwnd, out var rc);
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
        if (w <= 0 || h <= 0) return null;
        var screen = Native.GetDC(0);
        var mem = Native.CreateCompatibleDC(screen);
        var bmp = Native.CreateCompatibleBitmap(screen, w, h);
        var old = Native.SelectObject(mem, bmp);
        Native.PrintWindow(hwnd, mem, 1 | 4);   // PW_CLIENTONLY | PW_RENDERFULLCONTENT
        var bi = new Native.BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var px = new byte[w * h * 4];
        Native.GetDIBits(mem, bmp, 0, (uint)h, px, ref bi, 0);
        Native.SelectObject(mem, old); Native.DeleteObject(bmp); Native.DeleteDC(mem); Native.ReleaseDC(0, screen);
        for (var i = 3; i < px.Length; i += 4) px[i] = 255;
        return new Shot(w, h, px);
    }

    public static async Task SavePngAsync(Shot s, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir != null) Directory.CreateDirectory(dir);
        using var bmp = CanvasBitmap.CreateFromBytes(CanvasDevice.GetSharedDevice(), s.Bgra, s.W, s.H, DirectXPixelFormat.B8G8R8A8UIntNormalized);
        await bmp.SaveAsync(path, CanvasBitmapFileFormat.Png);
    }
}
