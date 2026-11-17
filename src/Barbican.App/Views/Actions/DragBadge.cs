// Mirrors: Bulwark hooks/use-email-drag.ts:348 (createDragPreview: a small count badge set with dataTransfer.setDragImage at the cursor, not a picture of
// the row); WinUI 3 DragUIOverride.SetContentFromSoftwareBitmap (a target may replace what the drag shows). Owner actions.
//
// M2 hotfix 2 (the user: "it should be transparent or something, can't see what I'm dropping it to"): the platform's drag showed an opaque snapshot of
// the whole row over the folder under the cursor. When a drag starts the badge ("3 messages", a folder's name) is drawn once off screen into a bitmap,
// semi-transparent and offset below-right of the cursor; every drop target, and the shell around them, shows it instead of the snapshot.
// Windows only: Uno does not implement DragUIOverride content, so the Linux build keeps the platform's drag visual.


namespace Barbican.App.Views.Actions;

/// <summary>The small badge a drag shows at the cursor.</summary>
public static class DragBadge
{
    /// <summary>How far below and right of the cursor the badge sits, in view pixels.</summary>
    public const double Offset = 16;

    /// <summary>The badge's opacity (the folder under it shows through).</summary>
    public const byte Alpha = 0xD0;

    private static int _generation;
#if WINDOWS
    private static Canvas? _host;
    private static Border? _badge;
    private static TextBlock? _text;
    private static Windows.Graphics.Imaging.SoftwareBitmap? _bitmap;
#endif

    /// <summary>The shell's overlay canvas (not hit-testable): the badge is drawn there, far outside the window, never on screen.</summary>
    public static void Attach(Canvas host)
    {
        ArgumentNullException.ThrowIfNull(host);
#if WINDOWS
        _host = host;
#endif
    }

    /// <summary>A drag starts: draws the badge for <paramref name="label"/> (the bitmap is ready a frame later; until then the platform's visual shows).</summary>
    public static void Prepare(string label)
    {
        var generation = ++_generation;
#if WINDOWS
        _bitmap = null;
        if (_host is null)
        {
            return;
        }

        _ = RenderAsync(label, generation);
#else
        _ = label;
        _ = generation;
#endif
    }

    /// <summary>Shows the badge in place of the platform's drag visual (call from DragEnter and DragOver).</summary>
    public static void Apply(DragUIOverride ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
#if WINDOWS
        if (_bitmap is { } bitmap)
        {
            try
            {
                ui.SetContentFromSoftwareBitmap(bitmap, new Windows.Foundation.Point(0, 0));
                ui.IsContentVisible = true;
            }
            catch (ArgumentException)
            {
                // a bitmap the platform does not take: the default visual stays
            }
        }
#endif
    }

#if WINDOWS
    private static async Task RenderAsync(string label, int generation)
    {
        try
        {
            var badge = Badge(label);
            badge.UpdateLayout();
            var rtb = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await rtb.RenderAsync(badge);
            var pixels = await rtb.GetPixelsAsync();
            if (generation != _generation || rtb.PixelWidth == 0 || rtb.PixelHeight == 0)
            {
                return;
            }

            _bitmap = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
                pixels, Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, rtb.PixelWidth, rtb.PixelHeight, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // no badge: the platform's visual shows
        }
    }

    private static Border Badge(string label)
    {
        if (_badge is null || _text is null)
        {
            _text = new TextBlock { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            _badge = new Border
            {
                // the transparent margin puts the visible pill below and right of the cursor (anchor 0,0 is the cursor)
                Padding = new Thickness(Offset, Offset + 2, 0, 0),
                Background = null,
                Child = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4, 10, 5), Child = _text },
                IsHitTestVisible = false,
            };

            // the wrapper is moved far outside the window; the badge itself is rendered in its own coordinates, so it is never on screen
            var wrapper = new Canvas { RenderTransform = new TranslateTransform { X = -20000, Y = -20000 }, IsHitTestVisible = false };
            wrapper.Children.Add(_badge);
            _host!.Children.Add(wrapper);
        }

        var accent = Find("Accent", Windows.UI.Color.FromArgb(0xFF, 0x3B, 0x6E, 0xF5));
        var on = Find("OnAccent", Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        ((Border)_badge.Child).Background = new SolidColorBrush(Windows.UI.Color.FromArgb(Alpha, accent.R, accent.G, accent.B));
        _text.Foreground = new SolidColorBrush(on);
        _text.Text = label;
        return _badge;
    }

    private static Windows.UI.Color Find(string key, Windows.UI.Color fallback) =>
        Application.Current.Resources.TryGetValue(key, out var b) && b is SolidColorBrush s ? s.Color : fallback;
#endif
}
