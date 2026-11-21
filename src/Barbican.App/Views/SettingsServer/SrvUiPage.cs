// Mirrors: M6 plan section 4.1 (the page the ISettingsPageFactory builds once; the screen keeps it and only hides it) and D-M6-00 (the model raises PropertyChanged on the UI thread; the page redraws from the model, coalesced).
// A page keeps its state in its view model, so a redraw from the model loses nothing but focus. Owner srv-ui.

using Barbican.App.Theme;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.SettingsServer;

/// <summary>The scrolling body of a server-backed settings page.</summary>
internal abstract class SrvUiPage : Grid
{
    private bool _pending;
    private string? _anchor;
    private bool _loading;

    protected SrvUiPage(string id, string title, string subtitle)
    {
        Body = new StackPanel { Spacing = 8 };
        var heading = SrvUiKit.Title(title);
        Body.Children.Add(heading);
        Body.Children.Add(SrvUiKit.Dim(subtitle));
        var look = StyleState.Current;
        void Fit() => Settings.SetUiUi.FitColumn(Body, look.IsBulwark, look.SetPageMaxWidth - (2 * look.SetPagePadding.Left), 2 * look.SetPagePadding.Left);
        Body.Tag = (Action)Fit;
        void Frame()
        {
            Fit();
            // settings-app.tsx:1188-1194: max-w-3xl mx-auto px-6 py-6 and no visible title in Bulwark; the older styles keep the left column with the title
            Body.Margin = look.IsBulwark ? look.SetPagePadding : new Thickness(28, 20, 28, 28);
            Body.MaxWidth = look.IsBulwark ? look.SetPageMaxWidth - (2 * look.SetPagePadding.Left) : 820;
            Body.HorizontalAlignment = look.SetPageAlignment;
            heading.Visibility = look.SetPageTitleVisibility;
        }

        Frame();
        var style = look.Style;
        look.PropertyChanged += (_, _) =>
        {
            if (look.Style == style) return;
            style = look.Style;
            Frame();
            Invalidate();
        };
        AutomationProperties.SetAutomationId(this, "srv.page." + id);
        AutomationProperties.SetName(this, title);
        Children.Add(Settings.SetUiUi.Scroll(Body));
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
    public void Shown()
    {
        _loading = true;
        SrvUiKit.Run(async () =>
        {
            try
            {
                await LoadAsync().ConfigureAwait(true);
            }
            finally
            {
                // the rows drawn from the loaded data move what is below them: the anchor settles once that draw is done
                if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FinishReveal)) _loading = false;
            }
        });
    }

    private void FinishReveal()
    {
        _loading = false;
        TryReveal();
    }

    /// <summary>
    /// Scrolls to the element whose automation id is the anchor (or <see cref="AnchorId"/> of it). A page that has not drawn yet keeps the anchor and scrolls
    /// once it has: the sidebar's folder and tag gears open Folders and tags at their section (sidebar.tsx:808-813).
    /// </summary>
    public void Reveal(string anchor)
    {
        _anchor = anchor;
        TryReveal();
    }

    /// <summary>The automation id an anchor names (a page maps its short anchors, such as "folders", to the ids it draws).</summary>
    protected virtual string AnchorId(string anchor) => anchor;

    private void TryReveal()
    {
        if (_anchor is not { } anchor || Find(Body, AnchorId(anchor)) is not { } found)
        {
            return;
        }

        if (!_loading) _anchor = null;
        _ = found.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => found.StartBringIntoView(new Microsoft.UI.Xaml.BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = false }));
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
                TryReveal();
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
