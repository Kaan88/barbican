// Mirrors: M6 plan section 4.1 (the page the ISettingsPageFactory builds once; the screen keeps it and only hides it) and D-M6-00 (the model raises PropertyChanged on the UI thread; the page redraws from the model, coalesced).
// A page keeps its state in its view model, so a redraw from the model loses nothing but focus. Owner srv-ui.

using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.SettingsServer;

/// <summary>The scrolling body of a server-backed settings page.</summary>
internal abstract class SrvUiPage : Grid
{
    private bool _pending;

    protected SrvUiPage(string id, string title, string subtitle)
    {
        Body = new StackPanel { Spacing = 8, Margin = new Thickness(28, 20, 28, 28), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        Body.Children.Add(SrvUiKit.Title(title));
        Body.Children.Add(SrvUiKit.Dim(subtitle));
        AutomationProperties.SetAutomationId(this, "srv.page." + id);
        AutomationProperties.SetName(this, title);
        Children.Add(new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }

    /// <summary>The page's own content goes under the title and the subtitle.</summary>
    protected StackPanel Body { get; }

    /// <summary>Draws the model; called after every change, on the UI thread.</summary>
    protected abstract void Render();

    /// <summary>Starts the load of the page's data (off the UI thread).</summary>
    protected abstract Task LoadAsync();

    /// <summary>First draw and first load, when the factory built the page (the screen may show a page without calling OnShown).</summary>
    public void Start()
    {
        Invalidate();
        Shown();
    }

    /// <summary>Called each time the page is shown.</summary>
    public void Shown() => SrvUiKit.Run(LoadAsync);

    /// <summary>Scrolls to the element whose automation id is <paramref name="anchor"/>.</summary>
    public void Reveal(string anchor)
    {
        if (Find(Body, anchor) is { } found)
        {
            found.StartBringIntoView();
        }
    }

    /// <summary>Redraws once, after the current event finished (many changes in a row draw once).</summary>
    protected void Invalidate()
    {
        if (_pending)
        {
            return;
        }

        _pending = true;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _pending = false;
                Render();
            }))
        {
            _pending = false;
        }
    }

    /// <summary>Empties the content below the title and the subtitle.</summary>
    protected void Reset()
    {
        while (Body.Children.Count > 2)
        {
            Body.Children.RemoveAt(2);
        }
    }

    private static FrameworkElement? Find(DependencyObject root, string id)
    {
        if (root is FrameworkElement fe && string.Equals(AutomationProperties.GetAutomationId(fe), id, StringComparison.Ordinal))
        {
            return fe;
        }

        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (Find(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i), id) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }
}
