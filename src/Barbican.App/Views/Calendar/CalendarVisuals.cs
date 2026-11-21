// Mirrors: Rust calendar.rs:68 snap (calendar colours drawn as the design's meaning tints: reds and pinks rose, oranges and yellows amber, greens and teals mint, blues sky; anything else keeps its own colour with the generic tint),
// :603 ev; design BNCalendar (tinted cards: tint background, tint foreground, 12 px radius); B-CAL-059 (past events dimmed, the selected chip outlined in its tint-fg). Palette brushes are the live ones, so a theme flip needs no rebuild.

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Core.Calendar;
using Barbican.Presentation.Calendar.Grid;
using Microsoft.UI.Text;
using Windows.UI.Text;
using Windows.UI;

namespace Barbican.App.Views.Calendar;

/// <summary>Colours and small builders shared by the calendar views (code-built controls: the grids are drawn, not templated).</summary>
internal static class CalendarVisuals
{
    private static readonly Dictionary<string, SolidColorBrush> Generic = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, SolidColorBrush> Solid = new(StringComparer.Ordinal);
    private static readonly Dictionary<(string Hex, uint Ground), SolidColorBrush> Mixed = [];

    /// <summary>The Bulwark shape and colour language is on (the default theme).</summary>
    public static bool Bulwark => ViewHost.Find<StyleState>()?.IsBulwark == true;

    public static SolidColorBrush Palette(string key) => AppResources.Brush(key);

    public static Color Parse(string hex)
    {
        var h = CalendarRows.Hex(hex) ?? "#3b82f6";
        return Color.FromArgb(0xFF, Convert.ToByte(h.Substring(1, 2), 16), Convert.ToByte(h.Substring(3, 2), 16), Convert.ToByte(h.Substring(5, 2), 16));
    }

    /// <summary>The solid brush of a calendar colour (the sidebar square, the dot).</summary>
    public static Brush SolidBrush(string hex)
    {
        var key = CalendarRows.Hex(hex) ?? "#3b82f6";
        if (!Solid.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(Parse(key));
            Solid[key] = brush;
        }

        return brush;
    }

    /// <summary>Which of the four meaning tints a colour belongs to (0 rose, 1 amber, 2 mint, 3 sky), or -1 for a colour that keeps its own (Rust snap).</summary>
    public static int Family(string hex)
    {
        var c = Parse(hex);
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var mx = Math.Max(r, Math.Max(g, b));
        var mn = Math.Min(r, Math.Min(g, b));
        if (mx - mn < 0.15)
        {
            return -1;
        }

        var d = mx - mn;
        double h;
        if (Math.Abs(mx - r) < 1e-9)
        {
            h = 60.0 * (((g - b) / d % 6.0 + 6.0) % 6.0);
        }
        else if (Math.Abs(mx - g) < 1e-9)
        {
            h = 60.0 * (((b - r) / d) + 2.0);
        }
        else
        {
            h = 60.0 * (((r - g) / d) + 4.0);
        }

        return (int)h switch
        {
            >= 0 and <= 14 => 0,
            >= 320 => 0,
            >= 15 and <= 69 => 1,
            >= 70 and <= 189 => 2,
            >= 190 and <= 254 => 3,
            _ => -1,
        };
    }

    /// <summary>The text colour that reads on a solid calendar colour (Bulwark lib/calendar-colors readableTextOn: dark text on a light colour).</summary>
    public static Brush ReadableOn(string hex)
    {
        var c = Parse(hex);
        var luminance = ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 255.0;
        return luminance > 0.6 ? new SolidColorBrush(Color.FromArgb(0xFF, 0x0A, 0x0A, 0x0A)) : new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
    }

    /// <summary>The calendar colour at 20 % over the page ground, opaque (event-card.tsx:104, color-mix(in srgb, colour 20%, background)); the ground is read live so a theme flip recolours it.</summary>
    public static Brush Mix(string hex)
    {
        var key = CalendarRows.Hex(hex) ?? "#3b82f6";
        var ground = Palette("Bg").Color;
        var id = ((uint)ground.R << 16) | ((uint)ground.G << 8) | ground.B;
        if (!Mixed.TryGetValue((key, id), out var brush))
        {
            var c = Parse(key);
            byte Blend(byte top, byte under) => (byte)Math.Round((top * 0.2) + (under * 0.8));
            brush = new SolidColorBrush(Color.FromArgb(0xFF, Blend(c.R, ground.R), Blend(c.G, ground.G), Blend(c.B, ground.B)));
            Mixed[(key, id)] = brush;
        }

        return brush;
    }

    /// <summary>The card colours of a calendar: tinted (the design's; Bulwark's 20 % mix with the page text), or the solid calendar colour with readable text.</summary>
    public static (Brush Background, Brush Foreground) Tint(string hex, bool tinted)
    {
        if (!tinted)
        {
            return (SolidBrush(hex), Bulwark ? ReadableOn(hex) : new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)));
        }

        if (Bulwark)
        {
            return (Mix(hex), Palette("Text"));
        }

        var family = Family(hex);
        if (family >= 0)
        {
            string[] bg = ["RoseBg", "AmberBg", "MintBg", "SkyBg"];
            string[] fg = ["RoseFg", "AmberFg", "MintFg", "SkyFg"];
            return (Palette(bg[family]), Palette(fg[family]));
        }

        var key = CalendarRows.Hex(hex) ?? "#3b82f6";
        if (!Generic.TryGetValue(key, out var generic))
        {
            var c = Parse(key);
            generic = new SolidColorBrush(Color.FromArgb(0x3A, c.R, c.G, c.B));
            Generic[key] = generic;
        }

        return (generic, Palette("Text"));
    }

    /// <summary>A line icon; "ChevronLeft" is the right chevron turned round (the token set has no left one).</summary>
    public static Barbican.App.Controls.SIcon Icon(string kind, double size = 16, double stroke = 1.8)
    {
        // the token set has no left chevron and no vertical dots: the right chevron turned round, the dots turned up
        var left = string.Equals(kind, "ChevronLeft", StringComparison.Ordinal);
        var vertical = string.Equals(kind, "MoreVertical", StringComparison.Ordinal);
        var icon = new Barbican.App.Controls.SIcon { Kind = left ? "ChevronRight" : vertical ? "More" : kind, Size = size, StrokeWidth = stroke };
        if (left || vertical)
        {
            icon.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            icon.RenderTransform = new RotateTransform { Angle = left ? 180 : 90 };
        }

        return icon;
    }

    /// <summary>Lucide "menu" (three bars, the sidebar toggle of calendar-toolbar.tsx:420), which the token set lacks; <paramref name="size"/> is its box.</summary>
    public static StackPanel MenuIcon(double size = 16)
    {
        var bars = new StackPanel { Spacing = size * 0.19, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        for (var i = 0; i < 3; i++)
        {
            bars.Children.Add(new Border { Width = size * 0.83, Height = Math.Max(1.5, size * 0.1), CornerRadius = new CornerRadius(1), Background = Palette("Dim") });
        }

        return bars;
    }

    public static TextBlock Text(string text, double size, FontWeight? weight = null, Brush? foreground = null, bool trim = true)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = foreground ?? Palette("Body"),
            TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            IsHitTestVisible = false,
        };
    }

    /// <summary>One event card of the time grid and the strip: title, time line, tinted, dimmed when past, outlined when selected.</summary>
    public static Border Card(CalendarOccurrence o, string hex, CalendarViewSettings settings, bool selected, bool past, bool showTime, double radius = 10)
    {
        var bulwark = Bulwark;
        if (bulwark)
        {
            radius = 2; // rounded-xs (event-card.tsx:470)
        }

        var (bg, fg) = Tint(hex, settings.Core.TintedEvents);
        var stack = new StackPanel { Spacing = 1, IsHitTestVisible = false };
        var title = o.Kind == CalendarOccurrenceKind.Task ? AgendaRules.TaskTitle(o.Title) : o.Title;
        stack.Children.Add(Text(title, 12, bulwark ? FontWeights.Medium : FontWeights.Bold, fg));
        if (showTime && !o.AllDay)
        {
            var t = Text(CalendarFormat.Range(o.Start, o.End, settings.Core.TwelveHour), bulwark ? 10.5 : 11, FontWeights.Normal, fg);
            t.Opacity = 0.85;
            stack.Children.Add(t);
        }

        return new Border
        {
            Background = bg,
            CornerRadius = new CornerRadius(radius),
            Padding = bulwark ? new Thickness(6, 2, 6, 2) : new Thickness(8, 4, 8, 4),
            BorderBrush = selected ? (bulwark ? Palette("Accent") : fg) : null,
            BorderThickness = new Thickness(selected ? 2 : 0),
            Opacity = past && !bulwark ? 0.55 : 1, // Bulwark does not dim past events (event-card.tsx)
            Child = stack,
            IsHitTestVisible = false,
        };
    }
}
