// Mirrors: Bulwark components/plugins/plugin-dialog-host.tsx:150 and components/ui/toast.tsx:318 (the Puzzle icon beside "From the plugin <name>"). The shared icon set (Tokens.Icons, generated) has
// no puzzle piece, so the dialogs draw this one themselves from the same 24 x 24 line geometry as SIcon.

using Microsoft.UI.Xaml.Markup;


namespace Barbican.App.Views.Dialogs;

/// <summary>The add-on (puzzle piece) line glyph.</summary>
internal static class AddonGlyph
{
    private const string Data = "M15.39 4.39a1 1 0 0 0 1.68-.474 2.5 2.5 0 1 1 3.014 3.015 1 1 0 0 0-.474 1.68l1.683 1.682a2.414 2.414 0 0 1 0 3.414L19.61 15.39a1 1 0 0 1-1.68-.474 2.5 2.5 0 1 0-3.014 3.015 1 1 0 0 1 .474 1.68l-1.683 1.682a2.414 2.414 0 0 1-3.414 0L8.61 19.61a1 1 0 0 0-1.68.474 2.5 2.5 0 1 1-3.014-3.015 1 1 0 0 0 .474-1.68l-1.683-1.682a2.414 2.414 0 0 1 0-3.414L4.39 8.61a1 1 0 0 1 1.68.474 2.5 2.5 0 1 0 3.014-3.015 1 1 0 0 1-.474-1.68l1.683-1.682a2.414 2.414 0 0 1 3.414 0z";

    /// <summary>A square glyph of <paramref name="size"/> px stroked with <paramref name="stroke"/>.</summary>
    public static UIElement Create(double size, Brush stroke)
    {
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{Data}' />");
        path.Stroke = stroke;
        path.StrokeThickness = 2;
        path.StrokeLineJoin = PenLineJoin.Round;
        path.StrokeStartLineCap = PenLineCap.Round;
        path.StrokeEndLineCap = PenLineCap.Round;
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    }
}
