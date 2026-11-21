// Mirrors: Bulwark app/[locale]/apps/[pluginId]/[pageId] and components/plugins/plugin-page-app.tsx (the route of an add-on page, lib/plugin-sandbox/pages.ts:1-4: the add-on's `app-page` slot fills the content area beside the navigation rail,
// props { pageId, path }, catalogue plugin-slot-catalog.ts:64).
// The surface is rail | the add-on's own sandboxed frame (PluginPageFrameHost mounts it). While the frame loads, or when the add-on is not running or does not draw the page, the area says so instead of staying blank.
// Builds without the Windows WebView draw only that message. It owns no add-on code and no input of its own.
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.App.Views.Rail;
using Barbican.Core.Plugins.Pages;
using Microsoft.UI.Xaml.Automation;

#if !WINDOWS
#pragma warning disable CA1822 // builds without the Windows WebView keep the same members and draw only the fixed message
#endif

namespace Barbican.App.Views.Plugins;

/// <summary>The screen of an add-on page, shown in place of the mail screen while a rail entry of an add-on is open.</summary>
public sealed class PluginPageScreen : UserControl
{
    private readonly PluginPageRegistry _pages = ViewHost.Get<PluginPageRegistry>();
    private readonly StyleState _look = ViewHost.Get<StyleState>();
#if WINDOWS
    private readonly PluginPageFrameHost _frame = new();
#else
    private readonly Border _surface = new();
    private readonly TextBlock _note = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 420 };
#endif

    public PluginPageScreen()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new RailView());
#if WINDOWS
        var surface = _frame.Surface;
        AutomationProperties.SetAutomationId(_frame.Note, "plugin-page.note");
        AutomationProperties.SetAutomationId(surface, "plugin-page.frame");
#else
        _note.HorizontalAlignment = HorizontalAlignment.Center;
        _note.VerticalAlignment = VerticalAlignment.Center;
        _note.Text = "This page is drawn by the add-on. Add-on page frames are not available in this build.";
        _surface.Child = _note;
        var surface = _surface;
        AutomationProperties.SetAutomationId(_note, "plugin-page.note");
#endif
        Grid.SetColumn(surface, 1);
        root.Children.Add(surface);
        Content = root;
        AutomationProperties.SetAutomationId(this, "plugin-page.screen");
        _look.PropertyChanged += (_, _) => ApplyLook();
        ApplyLook();
    }

    /// <summary>Shows the page behind <paramref name="key"/> ("package/page") opened with <paramref name="path"/>.</summary>
    public void Show(string key, string path)
    {
#if WINDOWS
        if (_pages.All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal)) is { } page)
        {
            _frame.Show(page.PackageId, page.Page.Id, path);
        }
        else
        {
            _frame.Stop();
            _frame.Note.Text = string.Empty;
        }
#endif
    }

    /// <summary>The page was left: its frame is removed (Bulwark unmounts the iframe).</summary>
    public void Hide()
    {
#if WINDOWS
        _frame.Stop();
#endif
    }

    private void ApplyLook()
    {
#if WINDOWS
        _frame.ApplyLook(_look);
#else
        var bento = _look.IsBento && !_look.IsBulwark;
        _surface.Background = _look.SurfaceBrush;
        _surface.BorderBrush = bento || _look.IsBulwark ? AppResources.Brush("Line") : null;
        _surface.BorderThickness = _look.IsBulwark ? new Thickness(0) : _look.PaneBorder;
        _surface.CornerRadius = _look.PaneRadius;
        _surface.Margin = bento ? new Thickness(4) : new Thickness(0);
        _note.Foreground = AppResources.Brush("Faint");
#endif
    }
}
