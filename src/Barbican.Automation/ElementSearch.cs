using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Barbican.Automation;

/// <summary>Finds elements of the window: by AutomationId, under a point, the first of a type; and their automation peers.</summary>
internal static class ElementSearch
{
    public static FrameworkElement? ById(DependencyObject? d, string id)
    {
        if (d is null)
        {
            return null;
        }

        if (d is FrameworkElement fe && string.Equals(AutomationProperties.GetAutomationId(fe), id, StringComparison.Ordinal))
        {
            return fe;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            if (ById(VisualTreeHelper.GetChild(d, i), id) is { } r)
            {
                return r;
            }
        }

        return null;
    }

    public static T? First<T>(DependencyObject? d)
        where T : class
    {
        if (d is null)
        {
            return null;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is T t)
            {
                return t;
            }

            if (First<T>(c) is { } r)
            {
                return r;
            }
        }

        return null;
    }

    public static IEnumerable<UIElement> At(UIElement root, double x, double y) =>
        VisualTreeHelper.FindElementsInHostCoordinates(new global::Windows.Foundation.Point(x, y), root, false);

    /// <summary>Peers are created lazily: FromElement alone returns null until UIA (or someone) asks.</summary>
    public static AutomationPeer? Peer(FrameworkElement fe) =>
        FrameworkElementAutomationPeer.FromElement(fe) ?? FrameworkElementAutomationPeer.CreatePeerForElement(fe);

    /// <summary>The element at the point (or its nearest ancestor) whose peer supports one of the patterns.</summary>
    public static (FrameworkElement? Element, AutomationPeer? Peer) Target(UIElement root, double x, double y, params PatternInterface[] wanted)
    {
        foreach (var hit in At(root, x, y))
        {
            for (DependencyObject? d = hit; d is not null; d = VisualTreeHelper.GetParent(d))
            {
                if (d is FrameworkElement fe && Peer(fe) is { } peer && wanted.Any(w => peer.GetPattern(w) is not null))
                {
                    return (fe, peer);
                }
            }
        }

        return (null, null);
    }
}
