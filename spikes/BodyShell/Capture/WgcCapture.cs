#if WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace Barbican.Spike.BodyShell.Capture;

/// <summary>
/// Windows.Graphics.Capture on the spike's own window: every frame DWM composes for it, WebView2
/// and popups included, with its composition time (SystemRelativeTime, QPC). Works for a window
/// that is off-screen (it is the window's redirection surface that is captured, not the screen).
/// Pixels come back through SoftwareBitmap.CreateCopyFromSurfaceAsync on a consumer task, in order.
/// </summary>
internal sealed partial class WgcCapture : IFrameSource
{
    private static readonly Guid GraphicsCaptureItemInteropId = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid GraphicsCaptureItemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid DxgiDeviceId = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    private readonly GraphicsCaptureItem _item;
    private readonly IDirect3DDevice _device;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private Channel<Direct3D11CaptureFrame>? _frames;
    private Task? _consumer;

    public WgcCapture(nint hwnd)
    {
        _item = CreateItem(hwnd);
        _device = CreateDevice();
    }

    public string Name => "Windows.Graphics.Capture (window)";

    public void Start(Action<CapturedFrame> sink)
    {
        _frames = Channel.CreateUnbounded<Direct3D11CaptureFrame>(new UnboundedChannelOptions { SingleReader = true });
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 4, _item.Size);
        _pool.FrameArrived += (p, _) =>
        {
            var frame = p.TryGetNextFrame();
            if (frame is not null && !_frames.Writer.TryWrite(frame))
            {
                frame.Dispose();
            }
        };
        _session = _pool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = false;
        try
        {
            _session.IsBorderRequired = false;
        }
        catch (Exception)
        {
            // Older builds: the yellow border is drawn around the window, which is off-screen anyway.
        }

        var reader = _frames.Reader;
        _consumer = Task.Run(async () =>
        {
            await foreach (var frame in reader.ReadAllAsync())
            {
                using (frame)
                {
                    using var bmp = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied);
                    var w = bmp.PixelWidth;
                    var h = bmp.PixelHeight;
                    var bytes = new byte[w * h * 4];
                    bmp.CopyToBuffer(bytes.AsBuffer());
                    sink(new CapturedFrame(frame.SystemRelativeTime.Ticks, w, h, w * 4, bytes));
                }
            }
        });
        _session.StartCapture();
    }

    public async Task StopAsync()
    {
        _session?.Dispose();
        _session = null;
        _pool?.Dispose();
        _pool = null;
        _frames?.Writer.TryComplete();
        if (_consumer is not null)
        {
            await _consumer.WaitAsync(TimeSpan.FromSeconds(10));
        }

        _consumer = null;
    }

    public CapturedFrame? Grab() => null;

    public void Dispose()
    {
        _session?.Dispose();
        _pool?.Dispose();
        _frames?.Writer.TryComplete();
        _device.Dispose();
    }

    private static unsafe GraphicsCaptureItem CreateItem(nint hwnd)
    {
        var factory = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem", GraphicsCaptureItemInteropId);
        var self = factory.ThisPtr;
        var vtbl = *(nint**)self;
        var createForWindow = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)vtbl[3];
        var iid = GraphicsCaptureItemId;
        nint raw;
        Marshal.ThrowExceptionForHR(createForWindow(self, hwnd, &iid, &raw));
        try
        {
            return GraphicsCaptureItem.FromAbi(raw);
        }
        finally
        {
            Marshal.Release(raw);
        }
    }

    private static IDirect3DDevice CreateDevice()
    {
        const int DriverTypeHardware = 1;
        const uint CreateBgraSupport = 0x20;
        const uint SdkVersion = 7;
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, DriverTypeHardware, 0, CreateBgraSupport, 0, 0, SdkVersion, out var d3d, out _, out var context));
        try
        {
            var dxgiId = DxgiDeviceId;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, in dxgiId, out var dxgi));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
                try
                {
                    return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                }
                finally
                {
                    Marshal.Release(inspectable);
                }
            }
            finally
            {
                Marshal.Release(dxgi);
            }
        }
        finally
        {
            Marshal.Release(context);
            Marshal.Release(d3d);
        }
    }

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint immediateContext);

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
#endif
