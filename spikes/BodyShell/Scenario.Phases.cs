using Barbican.Spike.BodyShell.Capture;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using Windows.UI;

namespace Barbican.Spike.BodyShell;

/// <summary>Captured phases: message switching (blank frames, header/body skew), the dark pass (white flash), overlays over the body (airspace).</summary>
internal sealed partial class Scenario
{
    private const int Switches = 24;

    private static ColorClass ClassA => new("A", A);
    private static ColorClass ClassB => new("B", B);
    private static ColorClass ClassTile => new("tile", SpikeRoot.TileColor, 6);
    private static ColorClass ClassWhite => new("white", Color.FromArgb(255, 255, 255, 255), 24);
    private static ColorClass ClassBlack => new("black", Color.FromArgb(255, 0, 0, 0), 6);
    private static ColorClass ClassMagenta => new("magenta", MagentaColor, 24);
    private static ColorClass ClassPopup => new("popup", PopupColor, 8);

    private async Task SwitchPhaseAsync()
    {
        await ShowAsync(MailDocument.Solid(ColorA, "A"), ColorA, false, ShellHost.Transport.Script, A, "A");
        var regions = new[] { new Region("body", Deflate(SpikeRoot.BodyRect, 8)), new Region("swatch", Deflate(SpikeRoot.SwatchRect, 4)) };
        var classes = new[] { ClassA, ClassB, ClassTile, ClassWhite, ClassBlack };
        var rec = Recorder("switch", regions, classes, s => s.Located && s.Dominant("body") is not ("A" or "B") ? "bad" : null);
        var readyMs = new List<double>();
        var frames = await CaptureAsync(rec, async () =>
        {
            for (var i = 0; i < Switches; i++)
            {
                var toB = i % 2 == 0;
                var ms = await ShowAsync(MailDocument.Solid(toB ? ColorB : ColorA, toB ? "B" : "A"), toB ? ColorB : ColorA, false, ShellHost.Transport.Script, toB ? B : A, toB ? "B" : "A");
                if (ms is { } m)
                {
                    readyMs.Add(m);
                }

                await Task.Delay(160);
            }
        });

        var located = frames.Where(f => f.Located).ToList();
        var bad = located.Count(f => f.Dominant("body") is not ("A" or "B"));
        var skew = 0;
        var skewRun = 0;
        var maxSkewFrames = 0;
        foreach (var f in located)
        {
            var b = f.Dominant("body");
            var s = f.Dominant("swatch");
            if (b is "A" or "B" && s is "A" or "B" && b != s)
            {
                skew++;
                skewRun++;
                maxSkewFrames = Math.Max(maxSkewFrames, skewRun);
            }
            else
            {
                skewRun = 0;
            }
        }

        var metrics = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["switches"] = Switches,
            ["ready-ms-median"] = readyMs.Count == 0 ? -1 : readyMs.Order().ElementAt(readyMs.Count / 2),
            ["ready-ms-max"] = readyMs.Count == 0 ? -1 : readyMs.Max(),
            ["blank-or-flash-frames"] = bad,
            ["header-body-skew-frames"] = skew,
            ["header-body-skew-max-consecutive"] = maxSkewFrames,
        };
        Phase($"switch A/B x{Switches} (ready/reveal)", bad == 0 && readyMs.Count == Switches, frames, $"{bad} frames neither A nor B; header and body disagree in {skew} frames (max {maxSkewFrames} in a row)", metrics);
    }

    private async Task DarkPhaseAsync()
    {
        await ShowAsync(MailDocument.Solid(ColorA, "A"), ColorA, false, ShellHost.Transport.Script, A, "A");
        var dark = MailDocument.Dark("<div style=\"background:#ffffff;color:#111111;padding:40px;min-height:600px\"><h1>Light mail</h1><p>Recoloured by email_dark.js before it is shown.</p></div>", _darkJs);
        var regions = new[] { new Region("body", Deflate(SpikeRoot.BodyRect, 8)) };
        var classes = new[] { ClassA, ClassWhite, new ColorClass("dark", Color.FromArgb(255, 0x0A, 0x0A, 0x0A), 14), ClassTile };
        var rec = Recorder("dark", regions, classes, s => s.Located && s.Fraction("body", "white") > 0.02 ? "white" : null);
        double? ms = null;
        var frames = await CaptureAsync(rec, async () => ms = await ShowAsync(dark, MailDocument.DarkBackground, true, ShellHost.Transport.Script, B, "dark"), 600);
        var located = frames.Where(f => f.Located).ToList();
        var white = located.Count(f => f.Fraction("body", "white") > 0.02);
        var last = located.LastOrDefault();
        var finalWhite = last?.Fraction("body", "white") ?? 1;
        if (rec.Last is { } lf)
        {
            PngWriter.Save(lf, Path.Combine(_outDir, "dark-final.png"));
        }

        var metrics = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["ready-ms"] = ms ?? -1,
            ["white-flash-frames"] = white,
            ["final-white-fraction"] = Math.Round(finalWhite, 3),
        };
        Phase("dark pass (light mail shown dark, no white flash)", ms is not null && white == 0 && finalWhite < 0.05, frames, $"{white} frames with white in the body; final white fraction {finalWhite:F3}", metrics);
    }

    private async Task AirspaceFlyoutPhaseAsync()
    {
        await ShowAsync(MailDocument.Solid(Magenta, "magenta"), Magenta, false, ShellHost.Transport.Script, A, "magenta");
        await AirspacePopupMenuPhaseAsync();
#if WINDOWS
        // A real MenuFlyout is a windowed popup on WinUI 3 (its own top-level HWND, clamped onto a
        // monitor) even with ShouldConstrainToRootBounds=true: from an off-screen automation window
        // it shows on the user's screen. Only in a throwaway VM or desktop session (opt-in).
        if (Environment.GetEnvironmentVariable("BARBICAN_SPIKE_WINDOWED_MENUS") != "1")
        {
            _report.Notes.Add("MenuFlyout phase skipped on Windows: windowed popup would appear on the real screen (set BARBICAN_SPIKE_WINDOWED_MENUS=1 in a throwaway VM)");
            return;
        }
#endif
        await AirspaceMenuFlyoutPhaseAsync();
    }

    /// <summary>A menu in a constrained Popup: drawn in the window's own XAML tree, so it composes with the WebView2.</summary>
    private async Task AirspacePopupMenuPhaseAsync()
    {
        var panel = new StackPanel { Width = 220, Background = new SolidColorBrush(PopupColor), Padding = new Thickness(4) };
        foreach (var t in new[] { "Reply", "Reply all", "Forward", "Move to…", "Label", "Mark as unread", "Delete" })
        {
            panel.Children.Add(new TextBlock { Text = t, Foreground = new SolidColorBrush(Color.FromArgb(255, 0xD4, 0xD4, 0xD4)), Padding = new Thickness(12, 7, 12, 7) });
        }

        var at = new Point(SpikeRoot.BodyLeft + 200, SpikeRoot.BodyTop + 150);
        var popup = new Popup { ShouldConstrainToRootBounds = true, XamlRoot = _root.XamlRoot, HorizontalOffset = at.X, VerticalOffset = at.Y, Child = panel };
        popup.IsOpen = true;
#if WINDOWS
        var constrained = popup.IsConstrainedToRootBounds;
#else
        // Uno draws every popup inside the window (the property is not implemented there).
        var constrained = _root.XamlRoot is not null;
#endif
        if (!constrained)
        {
            // Same dispatcher turn as the open: closed before it is ever composed.
            popup.IsOpen = false;
            Check("constrained Popup stays in the window", false, "IsConstrainedToRootBounds=false; closed at once");
            return;
        }

        await Task.Delay(300);
        var r = Bounds(panel);
        popup.IsOpen = false;
        await Task.Delay(300);
        _report.Notes.Add($"constrained Popup menu: IsConstrainedToRootBounds={constrained}, rect={r}");
        await OverlayPhaseAsync("airspace: menu in a constrained Popup over WebView2", r, async () =>
        {
            popup.IsOpen = true;
            await Task.Delay(500);
            popup.IsOpen = false;
            await Task.Delay(300);
        });
    }

    private async Task AirspaceMenuFlyoutPhaseAsync()
    {
        var menu = BuildMenu();
        var at = new Point(200, 150);
        menu.ShowAt(_root.Body, new FlyoutShowOptions { Position = at, ShowMode = FlyoutShowMode.Standard });
        await Task.Delay(500);
        var (rect, constrained) = OpenPopupRect();
        menu.Hide();
        await Task.Delay(400);
        _report.Notes.Add($"MenuFlyout popup: IsConstrainedToRootBounds={constrained}, rect={rect}");
        if (rect is not { } r)
        {
            Check("MenuFlyout opens over the body", false, "no open popup found");
            return;
        }

        await OverlayPhaseAsync("airspace: MenuFlyout over WebView2", r, async () =>
        {
            menu.ShowAt(_root.Body, new FlyoutShowOptions { Position = at, ShowMode = FlyoutShowMode.Standard });
            await Task.Delay(700);
            menu.Hide();
            await Task.Delay(300);
        });
    }

    private async Task AirspaceDialogPhaseAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = "Delete this message?",
            Content = "Spike S2: a ContentDialog over the body view.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            Background = new SolidColorBrush(PopupColor),
        };
        _ = dialog.ShowAsync();
#if WINDOWS
        if (VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot).Any(p => !p.IsConstrainedToRootBounds))
        {
            // Same turn as the open: a windowed dialog would show on the real screen.
            dialog.Hide();
            Check("ContentDialog stays in the window", false, "windowed popup; closed at once");
            return;
        }
#endif

        await Task.Delay(700);
        FrameworkElement? box = null;
        foreach (var p in VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot))
        {
            box ??= p.Child is null ? null : FindDescendant(p.Child, "BackgroundElement");
        }

        if (box is null)
        {
            dialog.Hide();
            Check("ContentDialog opens over the body", false, "no BackgroundElement in the open popups");
            return;
        }

        var r = Bounds(box);
        dialog.Hide();
        await Task.Delay(500);
        _report.Notes.Add($"ContentDialog box rect={r}");
        await OverlayPhaseAsync("airspace: ContentDialog (with smoke layer) over WebView2", r, async () =>
        {
            _ = dialog.ShowAsync();
            await Task.Delay(800);
            dialog.Hide();
            await Task.Delay(400);
        }, scrim: true);
    }

    private async Task AirspaceCustomOverlayPhaseAsync()
    {
        var panelRect = new Rect(260, 220, 360, 200);
        var scrim = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) };
        var panel = new Border { Width = panelRect.Width, Height = panelRect.Height, Background = new SolidColorBrush(PopupColor), CornerRadius = new CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(panelRect.X, panelRect.Y, 0, 0) };
        _root.Overlay.Children.Clear();
        _root.Overlay.Children.Add(scrim);
        _root.Overlay.Children.Add(panel);
        await OverlayPhaseAsync("airspace: in-tree scrim + dialog panel (the planned dialog host) over WebView2", panelRect, async () =>
        {
            _root.Overlay.Visibility = Visibility.Visible;
            await Task.Delay(500);
            _root.Overlay.Visibility = Visibility.Collapsed;
            await Task.Delay(300);
        }, scrim: true);
    }

    /// <summary>
    /// Captures an overlay opening over the magenta body. Airspace means the web content paints over
    /// the overlay: pure magenta inside the overlay's box while the overlay is drawn. The body outside
    /// the overlay must stay on screen (magenta, or dimmed under a scrim), never blank.
    /// </summary>
    private async Task OverlayPhaseAsync(string name, Rect overlay, Func<Task> action, bool scrim = false)
    {
        var inner = Deflate(overlay, 10);
        var body = SpikeRoot.BodyRect;
        var outsideX = overlay.Right + 20;
        var outside = outsideX + 40 < body.Right ? new Rect(outsideX, body.Top + 20, body.Right - outsideX - 20, 120) : new Rect(body.Left + 20, body.Bottom - 120, 160, 100);
        var regions = new[] { new Region("overlay", inner), new Region("outside", outside) };
        // Magenta under any amount of black scrim: R and B equal-ish, G near zero.
        var dimmed = new ColorClass("dimmed", MagentaColor) { Test = (b, g, r) => r > 24 && Math.Abs(r - b) <= 16 && g <= r / 6 };
        var classes = new[] { ClassMagenta, ClassPopup, dimmed, ClassTile, ClassWhite, ClassBlack };
        var safeName = new string([.. name.Where(char.IsLetterOrDigit)]);
        var rec = Recorder(safeName, regions, classes, s =>
            !s.Located ? null
            : s.Fraction("overlay", "popup") > 0.05 && s.Fraction("overlay", "magenta") > 0.02 ? "punch-through"
            : s.Fraction("overlay", "popup") > 0.5 ? "open"
            : s.Fraction("outside", "magenta") + s.Fraction("outside", "dimmed") < 0.9 ? "body-lost"
            : null);
        var frames = await CaptureAsync(rec, action);
        var located = frames.Where(f => f.Located).ToList();
        var drawn = located.Where(f => f.Fraction("overlay", "popup") > 0.05).ToList();
        var punch = drawn.Count(f => f.Fraction("overlay", "magenta") > 0.02);
        var maxMagenta = drawn.Count == 0 ? 0 : drawn.Max(f => f.Fraction("overlay", "magenta"));
        var bodyLost = located.Count(f => f.Fraction("outside", "magenta") + f.Fraction("outside", "dimmed") < 0.9);
        var scrimLeak = scrim ? drawn.Count(f => f.Fraction("overlay", "popup") > 0.5 && f.Fraction("outside", "magenta") > 0.5) : 0;
        var metrics = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["frames-with-overlay"] = drawn.Count,
            ["punch-through-frames"] = punch,
            ["max-magenta-in-overlay"] = Math.Round(maxMagenta, 3),
            ["body-lost-frames"] = bodyLost,
            ["scrim-not-over-body-frames"] = scrimLeak,
        };
        var pass = drawn.Count > 0 && punch == 0 && bodyLost == 0 && scrimLeak == 0;
        Phase(name, pass, frames, $"overlay drawn in {drawn.Count} frames; web content over it in {punch} (max {maxMagenta:P1} of its box); body blank/hidden in {bodyLost}; scrim missing over the body in {scrimLeak}", metrics);
    }

    private static MenuFlyout BuildMenu()
    {
        var presenter = new Style(typeof(MenuFlyoutPresenter));
        presenter.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(PopupColor)));
        presenter.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        // Constrained: drawn inside the window's own XAML tree. The WinUI 3 default for a desktop
        // MenuFlyout is a windowed popup (its own HWND), which the system clamps onto a monitor:
        // for an off-screen automation window it appeared on the user's screen (S2 run 1). Hidden
        // automation must keep every popup constrained (docs/adr/0005).
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = presenter, ShouldConstrainToRootBounds = true };
        foreach (var t in new[] { "Reply", "Reply all", "Forward", "Move toâ€¦", "Label", "Mark as unread", "Delete" })
        {
            menu.Items.Add(new MenuFlyoutItem { Text = t });
        }

        return menu;
    }

    private (Rect? Rect, bool Constrained) OpenPopupRect()
    {
        foreach (var p in VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot))
        {
            if (p.Child is FrameworkElement fe && fe.ActualWidth > 0)
            {
#if WINDOWS
                return (Bounds(fe), p.IsConstrainedToRootBounds);
#else
                return (Bounds(fe), true);
#endif
            }
        }

        return (null, false);
    }

    private Rect Bounds(FrameworkElement fe) =>
        fe.TransformToVisual(_root).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));

    private static FrameworkElement? FindDescendant(DependencyObject parent, string name)
    {
        var n = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(parent, i);
            if (c is FrameworkElement fe && fe.Name == name)
            {
                return fe;
            }

            if (FindDescendant(c, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
