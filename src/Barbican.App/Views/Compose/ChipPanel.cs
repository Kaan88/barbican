// Mirrors: Bulwark components/email/email-composer.tsx recipient row (chips wrap onto further lines, the input takes what is left of the last line). WinUI has no wrapping panel in the box, so this lays children out left to right and wraps.
// Owner composer-ui.

using Windows.Foundation;

namespace Barbican.App.Views.Compose;

/// <summary>A panel that lays children out left to right and starts a new line when one does not fit; the last child (the input) fills the rest of the last line, at least <see cref="MinLast"/> wide.</summary>
public sealed class ChipPanel : Panel
{
    /// <summary>The narrowest the last child may get before it moves to a new line.</summary>
    public double MinLast { get; set; } = 120;

    public double Gap { get; set; } = 6;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width;
        double x = 0, lineHeight = 0, total = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(width, double.PositiveInfinity));
            var w = child.DesiredSize.Width;
            if (i == Children.Count - 1)
            {
                w = Math.Max(w, MinLast);
            }

            if (x > 0 && x + w > width)
            {
                total += lineHeight + Gap;
                x = 0;
                lineHeight = 0;
            }

            x += w + Gap;
            lineHeight = Math.Max(lineHeight, child.DesiredSize.Height);
        }

        return new Size(width, total + lineHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, lineHeight = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var last = i == Children.Count - 1;
            var w = last ? Math.Max(child.DesiredSize.Width, MinLast) : child.DesiredSize.Width;
            if (x > 0 && x + w > finalSize.Width)
            {
                y += lineHeight + Gap;
                x = 0;
                lineHeight = 0;
            }

            var fill = last ? Math.Max(w, finalSize.Width - x) : w;
            child.Arrange(new Rect(x, y, fill, child.DesiredSize.Height));
            x += w + Gap;
            lineHeight = Math.Max(lineHeight, child.DesiredSize.Height);
        }

        return finalSize;
    }
}
