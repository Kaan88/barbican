// Mirrors: Bulwark components/layout/navigation-rail.tsx:164-190 (storage quota ring: 32 px box, r 12, stroke 3, muted track, round caps, the arc coloured by
// level success / warning / destructive from lib/quota.ts:72-77, a 0.4375rem bold percentage inside); spec I-030; Rust sidebar storage tile (static).
// Without Leveled (Bento, Nordic) it is the prototype's thin accent ring around the account button.

using Barbican.App.Theme;
using Microsoft.UI.Xaml.Shapes;

namespace Barbican.App.Controls;

/// <summary>A ring that fills clockwise from the top with <see cref="Fraction"/> (0..1). A negative fraction (no quota limit) shows nothing. Not hit-testable.</summary>
public sealed partial class StorageRing : Grid
{
    public static readonly DependencyProperty FractionProperty =
        DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(StorageRing), new PropertyMetadata(-1.0, (d, _) => ((StorageRing)d).Draw()));

    public static readonly DependencyProperty RingSizeProperty =
        DependencyProperty.Register(nameof(RingSize), typeof(double), typeof(StorageRing), new PropertyMetadata(46.0, (d, _) => ((StorageRing)d).Draw()));

    public static readonly DependencyProperty LeveledProperty =
        DependencyProperty.Register(nameof(Leveled), typeof(bool), typeof(StorageRing), new PropertyMetadata(false, (d, _) => ((StorageRing)d).Draw()));

    private readonly Ellipse _track = new() { Stroke = (Brush)Application.Current.Resources["Track"] };
    private readonly Microsoft.UI.Xaml.Shapes.Path _arc = new() { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly TextBlock _label = new() { FontSize = 7, FontWeight = Microsoft.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };

    public StorageRing()
    {
        IsHitTestVisible = false;
        _label.Foreground = (Brush)Application.Current.Resources["Faint"];
        Children.Add(_track);
        Children.Add(_arc);
        Children.Add(_label);
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

    /// <summary>Bulwark: the arc takes the level colour and the percentage is written inside.</summary>
    public bool Leveled
    {
        get => (bool)GetValue(LeveledProperty);
        set => SetValue(LeveledProperty, value);
    }

    private void Draw()
    {
        var size = RingSize;
        Width = size;
        Height = size;
        var visible = Fraction >= 0;
        _track.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _arc.Visibility = visible && Fraction > 0 ? Visibility.Visible : Visibility.Collapsed;
        _label.Visibility = visible && Leveled ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
        {
            return;
        }

        // Bulwark: viewBox 32, r 12, stroke 3 (scaled to the box); the prototype: stroke 2.5 on the box edge
        var thickness = Leveled ? 3 * size / 32 : 2.5;
        var r = Leveled ? 12 * size / 32 : (size - thickness) / 2;
        _track.StrokeThickness = thickness;
        _arc.StrokeThickness = thickness;
        _arc.Stroke = Leveled ? StyleState.RingBrush(Fraction) : (Brush)Application.Current.Resources["Ring"];
        _label.Text = StyleState.Percent(Fraction);
        _track.Width = Leveled ? (2 * r) + thickness : size;
        _track.Height = _track.Width;
        var f = Math.Clamp(Fraction, 0, 0.9999);
        var c = size / 2;
        var angle = f * 2 * Math.PI;
        var end = new Windows.Foundation.Point(c + (r * Math.Sin(angle)), c - (r * Math.Cos(angle)));
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
