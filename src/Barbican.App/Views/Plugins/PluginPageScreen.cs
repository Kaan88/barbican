// Mirrors: Bulwark app/[locale]/apps/[pluginId]/[pageId] (the route of an add-on page, lib/plugin-sandbox/pages.ts:1-4: the add-on's `app-page` slot fills the content area beside the navigation rail).
// The page surface is the smallest one that fits the other app screens: rail | one surface that names the page, the add-on and the path it was opened with. The add-on's own frame (the `app-page` slot, catalogue
// plugin-slot-catalog.ts:64) is not mounted by any host yet (the slot mounts of M6b are open), so the surface says so instead of staying blank. It owns no add-on code and no input of its own.
using Barbican.App.Controls;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.App.Views.Rail;
using Barbican.Core.Plugins.Pages;
using Barbican.Presentation.Shell;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Plugins;

/// <summary>The screen of an add-on page, shown in place of the mail screen while a rail entry of an add-on is open.</summary>
public sealed class PluginPageScreen : UserControl
{
    private readonly PluginPageRegistry _pages = ViewHost.Get<PluginPageRegistry>();
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly Border _surface = new();
    private readonly SIcon _icon = new() { Size = 40 };
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _from = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _path = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _note = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 420 };

    public PluginPageScreen()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new RailView());
        var stack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(24) };
        stack.Children.Add(_icon);
        stack.Children.Add(_title);
        stack.Children.Add(_from);
        stack.Children.Add(_path);
        stack.Children.Add(_note);
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _from.HorizontalAlignment = HorizontalAlignment.Center;
        _path.HorizontalAlignment = HorizontalAlignment.Center;
        _note.HorizontalAlignment = HorizontalAlignment.Center;
        _note.Text = "This page is drawn by the add-on. Add-on page frames are not available in this build yet.";
        _surface.Child = stack;
        Grid.SetColumn(_surface, 1);
        root.Children.Add(_surface);
        Content = root;
        AutomationProperties.SetAutomationId(this, "plugin-page.screen");
        AutomationProperties.SetAutomationId(_title, "plugin-page.title");
        AutomationProperties.SetAutomationId(_from, "plugin-page.addon");
        AutomationProperties.SetAutomationId(_path, "plugin-page.path");
        AutomationProperties.SetAutomationId(_note, "plugin-page.note");
        _look.PropertyChanged += (_, _) => ApplyLook();
        ApplyLook();
    }

    /// <summary>Shows the page behind <paramref name="key"/> ("package/page") opened with <paramref name="path"/>.</summary>
    public void Show(string key, string path)
    {
        var page = _pages.All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));
        _icon.Kind = PluginPageIcons.KindFor(page?.Page.Icon);
        _title.Text = page?.Page.Title ?? string.Empty;
        _from.Text = page is null ? string.Empty : "From " + page.PackageName;
        _path.Text = path.Length == 0 ? string.Empty : "/" + path;
        _path.Visibility = path.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyLook()
    {
        var bento = _look.IsBento && !_look.IsBulwark;
        _surface.Background = _look.SurfaceBrush;
        _surface.BorderBrush = bento || _look.IsBulwark ? AppResources.Brush("Line") : null;
        _surface.BorderThickness = _look.IsBulwark ? new Thickness(0) : _look.PaneBorder;
        _surface.CornerRadius = _look.PaneRadius;
        _surface.Margin = bento ? new Thickness(4) : new Thickness(0);
        _icon.Foreground = AppResources.Brush("Faint");
        _title.Foreground = AppResources.Brush("Text");
        _from.Foreground = AppResources.Brush("Faint");
        _path.Foreground = AppResources.Brush("Faint");
        _note.Foreground = AppResources.Brush("Faint");
    }
}
