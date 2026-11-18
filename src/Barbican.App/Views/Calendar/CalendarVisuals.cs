// Mirrors: Rust calendar.rs:68 snap (calendar colours drawn as the design's meaning tints: reds and pinks rose, oranges and yellows amber, greens and teals mint, blues sky; anything else keeps its own colour with the generic tint),
// :603 ev; design BNCalendar (tinted cards: tint background, tint foreground, 12 px radius); B-CAL-059 (past events dimmed, the selected chip outlined in its tint-fg). Palette brushes are the live ones, so a theme flip needs no rebuild.

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

    /// <summary>The card colours of a calendar: tinted (the design's), or the solid calendar colour with light text.</summary>
    public static (Brush Background, Brush Foreground) Tint(string hex, bool tinted)
    {
        if (!tinted)
        {
            return (SolidBrush(hex), new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)));
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
        var left = string.Equals(kind, "ChevronLeft", StringComparison.Ordinal);
        var icon = new Barbican.App.Controls.SIcon { Kind = left ? "ChevronRight" : kind, Size = size, StrokeWidth = stroke };
        if (left)
        {
            icon.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            icon.RenderTransform = new RotateTransform { Angle = 180 };
        }

        return icon;
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
        var (bg, fg) = Tint(hex, settings.Core.TintedEvents);
        var stack = new StackPanel { Spacing = 1, IsHitTestVisible = false };
        var title = o.Kind == CalendarOccurrenceKind.Task ? AgendaRules.TaskTitle(o.Title) : o.Title;
        stack.Children.Add(Text(title, 12, FontWeights.Bold, fg));
        if (showTime && !o.AllDay)
        {
            var t = Text(CalendarFormat.Range(o.Start, o.End, settings.Core.TwelveHour), 11, FontWeights.Normal, fg);
            t.Opacity = 0.85;
            stack.Children.Add(t);
        }

        return new Border
        {
            Background = bg,
            CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(8, 4, 8, 4),
            BorderBrush = selected ? fg : null,
            BorderThickness = new Thickness(selected ? 2 : 0),
            Opacity = past ? 0.55 : 1,
            Child = stack,
            IsHitTestVisible = false,
        };
    }
}
