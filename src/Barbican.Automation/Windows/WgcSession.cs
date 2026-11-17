#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using Barbican.Automation.Capture;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Barbican.Automation.Windows;

/// <summary>
/// One Windows.Graphics.Capture session on the app window (docs/adr/0007 decision 5; port of
/// spikes/s4-automation/Capture.cs). Free-threaded frame pool: <see cref="Frame"/> runs on a pool
/// thread once per composed frame, and nothing arrives while the window is idle. The capture includes
/// the WebView2 body and in-window popups. Win2D only copies the surface to bytes.
/// </summary>
internal sealed partial class WgcSession : IDisposable
{
    private static readonly Guid InteropIid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid ItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    private readonly nint _hwnd;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly IDirect3DDevice _device = CanvasDevice.GetSharedDevice();
    private global::Windows.Graphics.SizeInt32 _size;

    public WgcSession(nint hwnd)
    {
        _hwnd = hwnd;
        var item = ItemForWindow(hwnd);
        _size = item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, item.Size);
        _pool.FrameArrived += OnFrameArrived;
        _session = _pool.CreateCaptureSession(item);
        TrySet(() => _session.IsCursorCaptureEnabled = false);
        TrySet(() => _session.IsBorderRequired = false);
        TrySet(() => _session.MinUpdateInterval = TimeSpan.FromMilliseconds(1));
        _session.StartCapture();
    }

    /// <summary>(frame system-relative time in ms, arrival time on the Stopwatch clock in ms, frame); the frame is valid only inside the call.</summary>
    public Action<double, double, Direct3D11CaptureFrame>? Frame { get; set; }

    /// <summary>Copies the frame's client area, downscaled by <paramref name="step"/>, to BGRA bytes. Call inside <see cref="Frame"/>.</summary>
    public Shot Read(Direct3D11CaptureFrame frame, int step = 1)
    {
        ArgumentNullException.ThrowIfNull(frame);
        using var bmp = CanvasBitmap.CreateFromDirect3D11Surface(CanvasDevice.GetSharedDevice(), frame.Surface);
        var all = bmp.GetPixelBytes();
        var fw = (int)bmp.SizeInPixels.Width;
        var fh = (int)bmp.SizeInPixels.Height;
        var (cx, cy, cw, ch) = Native.ClientInFrame(_hwnd);
        cx = Math.Clamp(cx, 0, fw);
        cy = Math.Clamp(cy, 0, fh);
        cw = Math.Min(cw, fw - cx);
        ch = Math.Min(ch, fh - cy);
        step = Math.Max(1, step);
        int ow = cw / step, oh = ch / step;
        var o = new byte[ow * oh * 4];
        for (var y = 0; y < oh; y++)
        {
            for (var x = 0; x < ow; x++)
            {
                var s = (((cy + (y * step)) * fw) + cx + (x * step)) * 4;
                var d = ((y * ow) + x) * 4;
                o[d] = all[s];
                o[d + 1] = all[s + 1];
                o[d + 2] = all[s + 2];
                o[d + 3] = 255;
            }
        }

        return new Shot(ow, oh, ow * 4, o);
    }

    public void Dispose()
    {
        _pool.FrameArrived -= OnFrameArrived;
        _session.Dispose();
        _pool.Dispose();
    }

    private static GraphicsCaptureItem ItemForWindow(nint hwnd)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString("Windows.Graphics.Capture.GraphicsCaptureItem", 44, out var cls));
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(cls, InteropIid, out var factory));
            try
            {
                var createForWindow = Marshal.GetDelegateForFunctionPointer<CreateForWindowFn>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 3 * IntPtr.Size));
                Marshal.ThrowExceptionForHR(createForWindow(factory, hwnd, ItemIid, out var item));
                try
                {
                    return GraphicsCaptureItem.FromAbi(item);
                }
                finally
                {
                    _ = Marshal.Release(item);
                }
            }
            finally
            {
                _ = Marshal.Release(factory);
            }
        }
        finally
        {
            _ = WindowsDeleteString(cls);
        }
    }

    private static void TrySet(Action set)
    {
        try
        {
            set();
        }
#pragma warning disable CA1031 // best effort: older Windows builds lack these session properties
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object args)
    {
        using var f = pool.TryGetNextFrame();
        if (f is null)
        {
            return;
        }

        var arrive = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
        if (f.ContentSize.Width != _size.Width || f.ContentSize.Height != _size.Height)
        {
            _size = f.ContentSize;
            pool.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, f.ContentSize);
        }

        Frame?.Invoke(f.SystemRelativeTime.TotalMilliseconds, arrive, f);
    }

    // IGraphicsCaptureItemInterop::CreateForWindow is the first method after IUnknown's three (slot 3).
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateForWindowFn(nint self, nint window, in Guid iid, out nint result);

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint classId, in Guid iid, out nint factory);

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string source, int length, out nint hstring);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint hstring);
}
#endif
