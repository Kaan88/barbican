// Mirrors: nothing in Bulwark; a small visual-tree helper of the menus stream (which element did a right-click land on, where is a row). Owner menus.

namespace Barbican.App.Views.Menus;

/// <summary>Walks the visual tree.</summary>
public static class VisualWalk
{
    /// <summary>The first element at or above <paramref name="start"/> whose data context is a <typeparamref name="T"/>.</summary>
    public static (FrameworkElement Element, T Context)? Up<T>(DependencyObject? start)
        where T : class
    {
        for (var node = start; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { DataContext: T context } element)
            {
                return (element, context);
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="node"/> is <paramref name="ancestor"/> or below it.</summary>
    public static bool IsInside(DependencyObject? node, DependencyObject ancestor)
    {
        for (; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The first element below <paramref name="root"/> (depth first) that matches.</summary>
    public static FrameworkElement? Find(DependencyObject root, Func<FrameworkElement, bool> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element && match(element))
            {
                return element;
            }

            if (Find(child, match) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
