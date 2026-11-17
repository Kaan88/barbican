// Mirrors: prototype/winui SIcon.cs; design/README.md ("1.6 to 1.8 px stroke line icons, paths in I"); Tokens.Icons (the mockups' I paths, generated).

using Barbican.Presentation.Theme;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Shapes;

namespace Barbican.App.Controls;

/// <summary>
/// A stroke line icon: the mockup path of <see cref="Kind"/> (a key of <see cref="Tokens.Icons"/>) in a 24 x 24 box, stroked with the
/// control's Foreground (so it follows a button's colour, and a theme flip, without any code). Not hit-testable.
/// </summary>
public sealed partial class SIcon : ContentControl
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(string), typeof(SIcon), new PropertyMetadata(string.Empty, (d, _) => ((SIcon)d).Build()));

    public static readonly DependencyProperty StrokeWidthProperty =
        DependencyProperty.Register(nameof(StrokeWidth), typeof(double), typeof(SIcon), new PropertyMetadata(1.7, (d, _) => ((SIcon)d).Build()));

    public SIcon()
    {
        Width = 18;
        Height = 18;
        IsHitTestVisible = false;
        IsTabStop = false;
    }

    /// <summary>A key of Tokens.Icons, for example "Mail" or "Inbox".</summary>
    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>The line width in the 24 x 24 box.</summary>
    public double StrokeWidth
    {
        get => (double)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    /// <summary>The same size on both sides.</summary>
    public double Size
    {
        get => Width;
        set
        {
            Width = value;
            Height = value;
        }
    }

    private void Build()
    {
        if (string.IsNullOrEmpty(Kind) || !Tokens.Icons.TryGetValue(Kind, out var data))
        {
            Content = null;
            return;
        }

        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{data}' />");
        path.StrokeThickness = StrokeWidth;
        path.StrokeLineJoin = PenLineJoin.Round;
        path.StrokeStartLineCap = PenLineCap.Round;
        path.StrokeEndLineCap = PenLineCap.Round;
        path.SetBinding(Shape.StrokeProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        Content = new Viewbox { Child = canvas };
    }
}
