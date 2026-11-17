// Mirrors: Bulwark components/layout/navigation-rail.tsx:177 (storage quota ring around the account button); spec I-030; Rust sidebar storage tile (static).

using Microsoft.UI.Xaml.Shapes;

namespace Barbican.App.Controls;

/// <summary>A thin ring that fills clockwise from the top with <see cref="Fraction"/> (0..1). A negative fraction (no quota limit) shows nothing. Not hit-testable.</summary>
public sealed partial class StorageRing : Grid
{
    public static readonly DependencyProperty FractionProperty =
        DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(StorageRing), new PropertyMetadata(-1.0, (d, _) => ((StorageRing)d).Draw()));

    public static readonly DependencyProperty RingSizeProperty =
        DependencyProperty.Register(nameof(RingSize), typeof(double), typeof(StorageRing), new PropertyMetadata(46.0, (d, _) => ((StorageRing)d).Draw()));

    private const double Thickness = 2.5;
    private readonly Ellipse _track = new() { StrokeThickness = Thickness, Stroke = (Brush)Application.Current.Resources["Track"] };
    private readonly Microsoft.UI.Xaml.Shapes.Path _arc = new() { StrokeThickness = Thickness, Stroke = (Brush)Application.Current.Resources["Ring"], StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

    public StorageRing()
    {
        IsHitTestVisible = false;
        Children.Add(_track);
        Children.Add(_arc);
        Draw();
    }

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public double RingSize
    {
        get => (double)GetValue(RingSizeProperty);
        set => SetValue(RingSizeProperty, value);
    }

    private void Draw()
    {
        var size = RingSize;
        Width = size;
        Height = size;
        var visible = Fraction >= 0;
        _track.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _arc.Visibility = visible && Fraction > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
        {
            return;
        }

        _track.Width = size;
        _track.Height = size;
        var f = Math.Clamp(Fraction, 0, 0.9999);
        var r = (size - Thickness) / 2;
        var c = size / 2;
        var angle = f * 2 * Math.PI;
        var end = new Windows.Foundation.Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(c, c - r), IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Windows.Foundation.Size(r, r),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = f > 0.5,
        });
        _arc.Data = new PathGeometry { Figures = { figure } };
        _arc.Width = size;
        _arc.Height = size;
    }
}
