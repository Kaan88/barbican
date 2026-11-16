using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Barbican.Core.Editor;
using Barbican.Editor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using Windows.Graphics;

namespace Barbican.Spikes.S3;

/// <summary>
/// Off-screen, never activated (no focus stolen, nothing on the user's screen): moved far outside
/// every monitor, hidden from Alt+Tab and the taskbar, shown without activation.
/// </summary>
internal sealed class HarnessWindow : Window
{
    private readonly string _outDir;
    private readonly TextBlock _xaml;
    private readonly SKXamlCanvas _skia;
    private readonly Canvas _overlay;
    private readonly Rectangle _caret;
    private readonly TextBox _ime;
    private readonly Grid _root;
    private readonly StringBuilder _log = new();
    private readonly LayoutStyle _style = new() { BodyFont = "Segoe UI", BaseSize = Sample.FontSize, Text = new SKColor(0xFF1B1F24), TextContrast = 1.6f };
    private readonly Doc _doc = Doc.FromText(Sample.Paragraph);
    private readonly DocLayout _layout;
    private Pos _caretPos = new(0, 0);
    private int _frames;

    public HarnessWindow(string outDir)
    {
        _outDir = outDir;
        _layout = new DocLayout(new RichTextKitEngine(), _style);
        _layout.Build(_doc, Sample.Width);
        var h = (float)Math.Ceiling(_layout.Height) + 8f;

        _xaml = new TextBlock
        {
            Text = Sample.Paragraph,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = Sample.FontSize,
            LineHeight = Sample.LineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap,
            Width = Sample.Width,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1B, 0x1F, 0x24)),
            VerticalAlignment = VerticalAlignment.Top,
        };
        _skia = new SKXamlCanvas { Width = Sample.Width, Height = h };
        _skia.PaintSurface += OnPaintSurface;

        _caret = new Rectangle { Width = 1.5, Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x2F, 0x6F, 0xDB)) };
        _ime = new TextBox { Width = 4, Height = 22, Opacity = 0, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, BorderThickness = new Thickness(0), IsSpellCheckEnabled = false };
        _ime.TextChanged += OnImeTextChanged;
        _ime.TextCompositionStarted += (_, e) => Log($"composition started at {e.StartIndex} len {e.Length}");
        _ime.TextCompositionChanged += (_, e) => Log($"composition changed at {e.StartIndex} len {e.Length}");
        _ime.TextCompositionEnded += (_, e) => Log($"composition ended at {e.StartIndex} len {e.Length}");
        _overlay = new Canvas { Width = Sample.Width, Height = h, IsHitTestVisible = false };
        _overlay.Children.Add(_caret);
        _overlay.Children.Add(_ime);

        var skiaCell = new Grid { Width = Sample.Width, Height = h, VerticalAlignment = VerticalAlignment.Top };
        skiaCell.Children.Add(_skia);
        skiaCell.Children.Add(_overlay);

        _root = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.White), Padding = new Thickness(16), ColumnSpacing = 24 };
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(skiaCell, 1);
        _root.Children.Add(_xaml);
        _root.Children.Add(skiaCell);
        Content = _root;
        Title = "Barbican S3 harness";
    }

    public void Start()
    {
        // Hidden automation rules (CLAUDE.md): never activate, never appear on a real monitor.
        AppWindow.IsShownInSwitchers = false;
        AppWindow.MoveAndResize(new RectInt32(-32000, -32000, 1000, 400));
        _root.Loaded += (_, _) => Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
        AppWindow.Show(activateWindow: false);
    }

    private void OnRendering(object? sender, object e)
    {
        if (++_frames != 5)
        {
            return;
        }

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            PlaceCaret();
            await ImePathAsync().ConfigureAwait(true);
            await Task.Delay(300).ConfigureAwait(true);
            await CaptureAsync().ConfigureAwait(true);
            File.WriteAllText(System.IO.Path.Combine(_outDir, "harness.log"), _log.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            File.WriteAllText(System.IO.Path.Combine(_outDir, "harness-error.txt"), ex.ToString());
        }
        finally
        {
            Application.Current.Exit();
        }
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var scale = (float)(e.Info.Width / _skia.ActualWidth);
        canvas.Clear(SKColors.White);
        canvas.Save();
        canvas.Scale(scale);
        _layout.Paint(canvas, _doc, Sel.Caret(_caretPos));
        canvas.Restore();
    }

    private void PlaceCaret()
    {
        var c = _layout.Caret(_caretPos, upstream: false);
        Canvas.SetLeft(_caret, c.X);
        Canvas.SetTop(_caret, c.Y);
        _caret.Height = c.Height;
        Canvas.SetLeft(_ime, c.X);
        Canvas.SetTop(_ime, c.Y);
    }

    /// <summary>
    /// The hidden-TextBox path without real input: text that lands in the TextBox (as committed IME
    /// text would) is moved into the Doc at the caret and the TextBox is emptied again.
    /// </summary>
    private async Task ImePathAsync()
    {
        var before = _doc.Blocks[0].Text.Length;
        _caretPos = new Pos(0, 5);
        PlaceCaret();
        var caretBefore = _layout.Caret(_caretPos, upstream: false);
        _ime.Text = "é中";
        await Task.Delay(200).ConfigureAwait(true);
        var after = _doc.Blocks[0].Text.Length;
        var caretAfter = _layout.Caret(_caretPos, upstream: false);
        var inserted = string.Equals(_doc.Blocks[0].Text.Substring(5, 2), "é中", StringComparison.Ordinal);
        Log(string.Create(CultureInfo.InvariantCulture, $"ime forward: block length {before} -> {after}, inserted at caret {inserted}, caret {_caretPos}, caret x {caretBefore.X:0.0} -> {caretAfter.X:0.0}, ime box at x {Canvas.GetLeft(_ime):0.0}, textbox emptied {_ime.Text.Length == 0}"));
        _doc.Delete(new Pos(0, 5), new Pos(0, 7));
        _caretPos = new Pos(0, 0);
        _layout.Build(_doc, Sample.Width);
        PlaceCaret();
        _skia.Invalidate();
    }

    private void OnImeTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ime.Text.Length == 0)
        {
            return;
        }

        _caretPos = _doc.InsertText(_caretPos, _ime.Text, _doc.Blocks[_caretPos.B].MarksAt(_caretPos.O));
        _ime.Text = "";
        _layout.Build(_doc, Sample.Width);
        PlaceCaret();
    }

    private async Task CaptureAsync()
    {
        var scale = _root.XamlRoot.RasterizationScale;
        Log(string.Create(CultureInfo.InvariantCulture, $"rasterization scale {scale}"));
        var left = await GrabAsync(_xaml).ConfigureAwait(true);
        var right = await GrabAsync(_skia).ConfigureAwait(true);
        Save(left, "textblock.png");
        Save(right, "skia.png");

        var ll = left.Lines();
        var rl = right.Lines();
        Log($"textblock lines {ll.Count}: {PixelStats.Json(ll)}");
        Log($"skia      lines {rl.Count}: {PixelStats.Json(rl)}");
        Log(string.Create(CultureInfo.InvariantCulture, $"ink total textblock {left.TotalInk()} skia {right.TotalInk()} ratio {(double)right.TotalInk() / left.TotalInk():0.000}"));
        Log(Diff(left, right, dy: 0));
        Log(Diff(left, right, dy: 1));
        CompareCarets(scale);
        Variants(left);
        Perf();
    }

    /// <summary>Layout and CPU raster cost of a long draft (60 paragraphs, 860 DIP wide, 150 % scale), as SKXamlCanvas would pay it.</summary>
    private void Perf()
    {
        var doc = new Doc(Enumerable.Range(0, 60).Select(_ => Barbican.Core.Editor.Block.Para(Sample.Paragraph)));
        var build = new List<double>();
        var paint = new List<double>();
        var edit = new List<double>();
        var layout = new DocLayout(new RichTextKitEngine(), _style);
        for (var i = 0; i < 15; i++)
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            layout.Build(doc, 860f);
            build.Add(System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);

            var info = new SKImageInfo(1290, 1600, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var bmp = new SKBitmap(info);
            using var canvas = new SKCanvas(bmp);
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            canvas.Clear(SKColors.White);
            canvas.Scale(1.5f);
            layout.Paint(canvas, doc, Sel.Caret(new Pos(0, 0)));
            canvas.Flush();
            paint.Add(System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);

            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var one = new RichTextKitEngine().Layout(doc.Blocks[30], 860f, _style);
            _ = one.Caret(100, upstream: false);
            edit.Add(System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
        }

        static double Median(List<double> v) => v.Order().ElementAt(v.Count / 2);
        Log(string.Create(CultureInfo.InvariantCulture, $"perf (median of 15): full layout 60 blocks {Median(build):0.00} ms, paint 1290x1600 px {Median(paint):0.00} ms, relayout one block {Median(edit):0.000} ms; doc height {layout.Height:0} DIP"));
    }

    /// <summary>The same paragraph rendered straight into bitmaps with each glyph setting, against the TextBlock capture.</summary>
    private void Variants(PixelStats textBlock)
    {
        var settings =
            from edging in new[] { SKFontEdging.Antialias, SKFontEdging.SubpixelAntialias }
            from hinting in new[] { SKFontHinting.None, SKFontHinting.Normal }
            from subpixel in new[] { true, false }
            select (edging, hinting, subpixel, contrast: 1f);
        var contrasts = new[] { 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.8f }.Select(k => (SKFontEdging.Antialias, SKFontHinting.Normal, true, k));
        foreach (var (edging, hinting, subpixel, contrast) in settings.Concat(contrasts))
        {
            {
                {
                    var style = _style with { Edging = edging, Hinting = hinting, SubpixelPositioning = subpixel, TextContrast = contrast };
                    var layout = new DocLayout(new RichTextKitEngine(), style);
                    layout.Build(_doc, Sample.Width);
                    var info = new SKImageInfo(textBlock.Width, textBlock.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                    using var bmp = new SKBitmap(info);
                    using (var canvas = new SKCanvas(bmp))
                    {
                        canvas.Clear(SKColors.White);
                        layout.Paint(canvas, _doc, Sel.Caret(new Pos(0, 0)));
                    }

                    var px = new PixelStats(bmp.Bytes, bmp.Width, bmp.Height);
                    var name = string.Create(CultureInfo.InvariantCulture, $"{edging}/{hinting}/{(subpixel ? "subpx" : "whole")}/k{contrast}");
                    Log(string.Create(CultureInfo.InvariantCulture, $"variant {name}: ink ratio {(double)px.TotalInk() / textBlock.TotalInk():0.000}; {Diff(textBlock, px, dy: 1)}"));
                    if (edging == SKFontEdging.Antialias && hinting == SKFontHinting.Normal && subpixel)
                    {
                        Save(px, string.Create(CultureInfo.InvariantCulture, $"variant-k{contrast}.png"));
                    }
                }
            }
        }
    }

    /// <summary>Per-character x of the TextBlock (TextPointer rects) against the RichTextKit caret x.</summary>
    private void CompareCarets(double scale)
    {
        var text = _doc.Blocks[0].Text;
        var start = _xaml.ContentStart;
        foreach (var shift in new[] { 0, 1, 2 })
        {
            var worst = 0.0;
            var sum = 0.0;
            var n = 0;
            var otherLine = 0;
            var worstAt = -1;
            for (var o = 0; o < text.Length; o++)
            {
                var tp = start.GetPositionAtOffset(o + shift, LogicalDirection.Forward);
                if (tp is null)
                {
                    break;
                }

                var r = tp.GetCharacterRect(LogicalDirection.Forward);
                var c = _layout.Caret(new Pos(0, o), upstream: false);
                if (Math.Abs(r.Y - c.Y) > 6)
                {
                    otherLine++;
                    continue;
                }

                var dx = Math.Abs(r.X - c.X);
                if (dx > worst)
                {
                    worst = dx;
                    worstAt = o;
                }

                sum += dx;
                n++;
            }

            Log(string.Create(CultureInfo.InvariantCulture, $"caret x vs TextBlock (TextPointer shift {shift}): {n} chars on the same line, mean |dx| {sum / Math.Max(n, 1):0.000} DIP, max {worst:0.000} DIP at {worstAt}; {otherLine} chars on another line (scale {scale})"));
        }

        var first = start.GetPositionAtOffset(1, LogicalDirection.Forward).GetCharacterRect(LogicalDirection.Forward);
        var c0 = _layout.Caret(new Pos(0, 0), upstream: false);
        Log(string.Create(CultureInfo.InvariantCulture, $"first char rect TextBlock ({first.X:0.00},{first.Y:0.00},{first.Height:0.00}) vs caret ({c0.X:0.00},{c0.Y:0.00},{c0.Height:0.00})"));
    }

    /// <summary>Ink difference with <paramref name="b"/> moved down by <paramref name="dy"/> pixels (baseline offset).</summary>
    private static string Diff(PixelStats a, PixelStats b, int dy)
    {
        var w = Math.Min(a.Width, b.Width);
        var h = Math.Min(a.Height, b.Height) - dy;
        long sum = 0;
        var big = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var d = Math.Abs(a.Ink(x, y + dy) - b.Ink(x, y));
                sum += d;
                if (d > 64)
                {
                    big++;
                }
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"pixel diff (dy {dy}) over {w}x{h}: mean |d| {(double)sum / (w * h):0.000}/255, pixels with |d|>64: {big}");
    }

    private static async Task<PixelStats> GrabAsync(UIElement element)
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(element);
        var buf = await rtb.GetPixelsAsync();
        return new PixelStats(buf.ToArray(), rtb.PixelWidth, rtb.PixelHeight);
    }

    private void Save(PixelStats p, string name)
    {
        var info = new SKImageInfo(p.Width, p.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap();
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(p.Bgra, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bmp.InstallPixels(info, handle.AddrOfPinnedObject(), info.RowBytes);
            using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(System.IO.Path.Combine(_outDir, name), data.ToArray());
        }
        finally
        {
            handle.Free();
        }
    }

    private void Log(string line) => _log.AppendLine(line);
}
