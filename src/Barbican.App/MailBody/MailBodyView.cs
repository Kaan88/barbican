// Mirrors: PORT-PLAN.md §3.6 (the mail body view: WebView2 on Windows, WebKitGTK on Linux); Rust mailview.rs (the view), app.slint r_loading
// (the thin loading bar), images_blocked (the external-content banner); Bulwark components/email/email-viewer.tsx:6418 (Load external
// content, spec I-209) and :6528 ("Shown as plain text" › Show formatted, spec I-214). shell-ui places this type in ReaderView.xaml's body
// slot. Owner body.
//
// The view holds the one warm WebView2 of BodyShell (it is never re-created: a second MailBodyView takes it over), the loading bar over its
// top edge (no layout shift) and the banners above it. The banners change in the same turn as the page they belong to is revealed.

using Barbican.App.Shell;
using Barbican.App.Theme;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Shapes;

namespace Barbican.App.MailBody;

/// <summary>The reader's message body view.</summary>
public sealed partial class MailBodyView : UserControl
{
    private readonly BodyShell? _shell;
    private readonly Grid _host = new();
    private readonly Rectangle _loading = new() { Height = 2, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Border _remoteBanner;
    private readonly Border _plainBanner;

    public MailBodyView()
    {
        _shell = ViewHost.Find<BodyShell>();
        _remoteBanner = Banner("External content is blocked.", "Load external content", "reader.load-external", () => _shell?.LoadExternalContent());
        _plainBanner = Banner("Shown as plain text.", "Show formatted", "reader.show-formatted", () => _shell?.ShowFormatted());
        var root = new Grid { RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_plainBanner, 1);
        Grid.SetRow(_host, 2);
        root.Children.Add(_remoteBanner);
        root.Children.Add(_plainBanner);
        root.Children.Add(_host);
        _loading.Fill = Brush("Accent");
        _host.Children.Add(_loading);
        Content = root;
        if (_shell is not null)
        {
            Attach(_shell);
        }
    }

    private void Attach(BodyShell shell)
    {
        var view = shell.View;
        if (view.Parent is Panel old)
        {
            old.Children.Remove(view);
        }

        _host.Children.Insert(0, view);
        shell.StateChanged += (_, _) => Sync(shell);
        Sync(shell);
    }

    private void Sync(BodyShell shell)
    {
        _remoteBanner.Visibility = shell.RemoteBlocked ? Visibility.Visible : Visibility.Collapsed;
        _plainBanner.Visibility = shell.ShownAsPlainText ? Visibility.Visible : Visibility.Collapsed;
        _loading.Visibility = shell.IsLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Border Banner(string text, string action, string automationId, Action click)
    {
        var button = new Button { Content = new TextBlock { Text = action }, Height = 30, Padding = new Thickness(12, 0, 12, 0) };
        if (Application.Current.Resources.TryGetValue("Button.Action", out var style) && style is Style s)
        {
            button.Style = s;
        }

        button.Click += (_, _) => click();
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, action);
        var label = new TextBlock { Text = text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = Brush("Body") };
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(button, 1);
        grid.Children.Add(label);
        grid.Children.Add(button);
        var border = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 6, 6, 6), Background = Brush("Tile2"), Child = grid, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(border, automationId + "-banner");
        AutomationProperties.SetName(border, text);
        return border;
    }

    private static SolidColorBrush Brush(string key) => Application.Current.Resources.ContainsKey(key) ? AppResources.Brush(key) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
}
