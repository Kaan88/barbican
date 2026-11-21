// A wrapping row (flex flex-wrap gap-1.5, search-filter-panel.tsx:105 and search-chips.tsx:78): children keep their size and break onto the next line when the width runs out. Owner search; WinUI has no WrapPanel.

namespace Barbican.App.Views.Search;

/// <summary>Lays children out left to right and wraps them onto new lines.</summary>
public sealed class WrapRow : Panel
{
    /// <summary>Gap between children on a line.</summary>
    public double HorizontalSpacing { get; set; }

    /// <summary>Gap between lines.</summary>
    public double VerticalSpacing { get; set; }

    protected override global::Windows.Foundation.Size MeasureOverride(global::Windows.Foundation.Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        double x = 0, lineHeight = 0, total = 0, used = 0;
        foreach (var child in Children)
        {
            child.Measure(new global::Windows.Foundation.Size(width, availableSize.Height));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                total += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
            }

            x += size.Width + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, size.Height);
            used = Math.Max(used, x - HorizontalSpacing);
        }

        return new global::Windows.Foundation.Size(used, total + lineHeight);
    }

    protected override global::Windows.Foundation.Size ArrangeOverride(global::Windows.Foundation.Size finalSize)
    {
        double x = 0, y = 0, lineHeight = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width)
            {
                y += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
            }

            child.Arrange(new global::Windows.Foundation.Rect(x, y, size.Width, size.Height));
            x += size.Width + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return new global::Windows.Foundation.Size(finalSize.Width, y + lineHeight);
    }
}
