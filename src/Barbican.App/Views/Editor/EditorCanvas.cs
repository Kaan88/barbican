// Mirrors: ADR 0006 section 5 (Windows host: SKXamlCanvas; Uno host: SKCanvasElement drawing straight into Uno's Skia compositor); M3 plan 6.3 step 2.
// The host only calls Draw with the canvas in device pixels and the scale to logical pixels; everything drawn is in Barbican.Editor.Layout.

using SkiaSharp;
#if WINDOWS
using SkiaSharp.Views.Windows;
#else
using Uno.WinUI.Graphics2DSK;
#endif

namespace Barbican.App.Views.Editor;

#if WINDOWS
/// <summary>The paint surface on the Windows head (SKXamlCanvas).</summary>
public sealed class EditorCanvas : SKXamlCanvas
{
    public EditorCanvas()
    {
        PaintSurface += (_, e) =>
        {
            var scale = ActualWidth > 0 ? (float)(e.Info.Width / ActualWidth) : 1f;
            Draw?.Invoke(e.Surface.Canvas, scale);
        };
    }

    /// <summary>Called for each frame with the canvas and the device-pixel scale; the callee clears and scales.</summary>
    public Action<SKCanvas, float>? Draw { get; set; }

    public void Redraw() => Invalidate();
}
#else
/// <summary>The paint surface on the Uno Skia head (SKCanvasElement).</summary>
public sealed class EditorCanvas : SKCanvasElement
{
    /// <summary>Called for each frame with the canvas and the device-pixel scale; the callee clears and scales.</summary>
    public Action<SKCanvas, float>? Draw { get; set; }

    protected override void RenderOverride(SKCanvas canvas, Windows.Foundation.Size area)
    {
        Draw?.Invoke(canvas, 1f);
    }

    public void Redraw() => Invalidate();
}
#endif
