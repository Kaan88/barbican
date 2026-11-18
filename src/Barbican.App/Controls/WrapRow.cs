using Windows.Foundation;

namespace Barbican.App.Controls;

/// <summary>Lays children left to right and wraps to a new row when the width runs out (the reader toolbar in a narrow pane: UI-SWEEP-1 R-04).</summary>
public sealed class WrapRow : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(WrapRow), new PropertyMetadata(8d, (d, _) => ((WrapRow)d).InvalidateMeasure()));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0, rowH = 0, y = 0, maxW = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var s = child.DesiredSize;
            if (x > 0 && x + s.Width > availableSize.Width)
            {
                y += rowH + Spacing;
                x = 0;
                rowH = 0;
            }

            x += s.Width + Spacing;
            rowH = Math.Max(rowH, s.Height);
            maxW = Math.Max(maxW, x - Spacing);
        }

        return new Size(maxW, y + rowH);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, rowH = 0, y = 0;
        foreach (var child in Children)
        {
            var s = child.DesiredSize;
            if (x > 0 && x + s.Width > finalSize.Width)
            {
                y += rowH + Spacing;
                x = 0;
                rowH = 0;
            }

            child.Arrange(new Rect(x, y, s.Width, s.Height));
            x += s.Width + Spacing;
            rowH = Math.Max(rowH, s.Height);
        }

        return finalSize;
    }
}
