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

    private Microsoft.UI.Xaml.Shapes.Path? _path;

    public SIcon()
    {
        Width = 18;
        Height = 18;
        IsHitTestVisible = false;
        IsTabStop = false;
        Loaded += (_, _) => BindStroke();
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
        _path = path;
        BindStroke();
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        Content = new Viewbox { Child = canvas };
    }

    // An icon with its own Foreground strokes in it. Otherwise it takes the colour of the content presenter it sits in (a button's, a folder row's):
    // Control.Foreground is not inherited by a control inside a template, so binding to the icon's own Foreground left it in the theme's default
    // text colour, white in a light window (the M1 hand-test finding "icons barely visible"; docs/milestones/M1-look-gap.md).
    private void BindStroke()
    {
        if (_path is null)
        {
            return;
        }

        object source = this;
        if (ReadLocalValue(ForegroundProperty) == DependencyProperty.UnsetValue)
        {
            for (var node = VisualTreeHelper.GetParent(this); node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is ContentPresenter presenter)
                {
                    source = presenter;
                    break;
                }
            }
        }

        _path.SetBinding(Shape.StrokeProperty, new Binding { Source = source, Path = new PropertyPath(nameof(Foreground)) });
    }
}
