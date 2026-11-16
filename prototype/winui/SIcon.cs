using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Barbican;

/// Stroke line icon from the mockups' I paths (24x24 viewbox, ~1.7px stroke).
public sealed partial class SIcon : Grid
{
    public static readonly Dictionary<string, string> I = new()
    {
        ["mail"] = "M4 4h16v16H4z M22 6l-10 7L2 6",
        ["cal"] = "M3 4h18v18H3z M16 2v4 M8 2v4 M3 10h18",
        ["users"] = "M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2 M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z",
        ["folder"] = "M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z",
        ["spark"] = "M12 3l1.9 5.8L20 11l-6.1 2.2L12 19l-1.9-5.8L4 11l6.1-2.2z",
        ["shield"] = "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z",
        ["inbox"] = "M22 12h-6l-2 3h-4l-2-3H2 M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z",
        ["file"] = "M14 2H6v20h12V8z M14 2v6h6",
        ["clock"] = "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M12 6v6l4 2",
        ["send"] = "M22 2 11 13 M22 2l-7 20-4-9-9-4 20-7z",
        ["archive"] = "M21 8v13H3V8 M1 3h22v5H1z M10 12h4",
        ["alert"] = "M12 9v4 M12 17h.01 M10.3 3.9 1.8 18h20.4L13.7 3.9a2 2 0 0 0-3.4 0z",
        ["trash"] = "M3 6h18 M19 6l-1 14H6L5 6 M8 6V4h8v2",
        ["reply"] = "M9 17l-5-5 5-5 M20 18v-2a4 4 0 0 0-4-4H4",
        ["fwd"] = "M15 17l5-5-5-5 M4 18v-2a4 4 0 0 1 4-4h12",
        // same family, for the prototype's toggles
        ["sun"] = "M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10z M12 1v2 M12 21v2 M4.2 4.2l1.4 1.4 M18.4 18.4l1.4 1.4 M1 12h2 M21 12h2 M4.2 19.8l1.4-1.4 M18.4 5.6l1.4-1.4",
        ["grid"] = "M3 3h7v7H3z M14 3h7v7h-7z M14 14h7v7h-7z M3 14h7v7H3z",
        ["pen"] = "M12 20h9 M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z",
        ["search"] = "M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16z M21 21l-4.35-4.35",
    };

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(SIcon), new PropertyMetadata(null, (d, _) => ((SIcon)d).Build()));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(SIcon), new PropertyMetadata(null, (d, _) => ((SIcon)d).Build()));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double Size { get => Width; set { Width = value; Height = value; } }

    public SIcon() { Width = 18; Height = 18; IsHitTestVisible = false; }

    void Build()
    {
        Children.Clear();
        if (Kind == null || !I.TryGetValue(Kind, out var d)) return;
        var p = (Path)XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{d}' />");
        p.Stroke = Stroke ?? Palette.B("Body");
        p.StrokeThickness = 1.7;
        p.StrokeLineJoin = PenLineJoin.Round;
        p.StrokeStartLineCap = PenLineCap.Round;
        p.StrokeEndLineCap = PenLineCap.Round;
        var c = new Canvas { Width = 24, Height = 24 };
        c.Children.Add(p);
        Children.Add(new Viewbox { Child = c });
    }

    public static SIcon Make(string kind, string brush, double size = 18) => new() { Kind = kind, Stroke = Palette.B(brush), Size = size };
}
