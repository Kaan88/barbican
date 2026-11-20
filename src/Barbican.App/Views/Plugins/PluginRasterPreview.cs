#if WINDOWS
// Mirrors Bulwark lib/plugin-sandbox/host-api-file-viewer.ts:26-50. Decode in memory, with a bound on both source and preview dimensions.
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Barbican.App.Views.Plugins;

internal static class PluginRasterPreview
{
    internal static async Task<UIElement?> LoadAsync(byte[] bytes, CancellationToken ct)
    {
        using var stream = new InMemoryRandomAccessStream();
        try
        {
            using var writer = new DataWriter(stream);
            writer.WriteBytes(bytes);
            _ = await writer.StoreAsync().AsTask(ct).ConfigureAwait(true);
            _ = writer.DetachStream();
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(true);
            var width = decoder.PixelWidth;
            var height = decoder.PixelHeight;
            if (width == 0 || height == 0 || (ulong)width * height > 36_000_000) return null;
            var scale = Math.Min(1, 1600d / Math.Max(width, height));
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, Math.Ceiling(width * scale)),
                ScaledHeight = (uint)Math.Max(1, Math.Ceiling(height * scale)),
            };
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(true);
            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(bitmap).AsTask(ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            var image = new Image { Source = source, Stretch = Stretch.Uniform, MaxHeight = 520, MaxWidth = 640 };
            AutomationProperties.SetAutomationId(image, "plugin.image.preview");
            AutomationProperties.SetName(image, "Add-on image preview");
            return image;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
#endif
