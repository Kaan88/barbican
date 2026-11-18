// Mirrors: PORT-PLAN.md §3.6 (the mail body view: WebView2 on Windows, WebKitGTK on Linux); Rust mailview.rs (the view), app.slint r_loading
// (the thin loading bar), images_blocked (the external-content banner); Bulwark components/email/email-viewer.tsx:6418 (Load external
// content, spec I-209) and :6528 ("Shown as plain text" › Show formatted, spec I-214). shell-ui places this type in ReaderView.xaml's body
// slot. M2: the reader banner stack (IReaderBannerSource, applied with the snapshot) and "Always for this sender" (I-209). Owner reader (M2).
//
// The view holds the one warm WebView2 of BodyShell (it is never re-created: a second MailBodyView takes it over), the loading bar over its
// top edge (no layout shift) and the banners above it. The banners change in the same turn as the page they belong to is revealed.

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Core.Mail.Trust;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Reader;
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
    private readonly StackPanel _readerBanners = new() { Spacing = 8 };
    private readonly ReaderViewModel? _reader;
    private readonly ReaderBannerActions? _actions;
    private readonly ITrustedSenders? _trusted;

    public MailBodyView()
    {
        _shell = ViewHost.Find<BodyShell>();
        _reader = ViewHost.Find<ReaderViewModel>();
        _actions = ViewHost.Find<ReaderBannerActions>();
        _trusted = ViewHost.Find<ITrustedSenders>();
        _remoteBanner = Banner("External content is blocked.", "Load external content", "reader.load-external", () => _shell?.LoadExternalContent());
        if (_trusted is not null && _remoteBanner.Child is Grid remoteGrid)
        {
            // I-209: "Always for this sender" trusts the sender (kept locally) and loads the content now
            var always = ActionButton("Always for this sender", "reader.trust-sender", () => _ = TrustSenderAsync());
            remoteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(always, 2);
            remoteGrid.Children.Add(always);
        }

        _plainBanner = Banner("Shown as plain text.", "Show formatted", "reader.show-formatted", () => _shell?.ShowFormatted());
        var root = new Grid { RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_remoteBanner, 1);
        Grid.SetRow(_plainBanner, 2);
        Grid.SetRow(_host, 3);
        AutomationProperties.SetAutomationId(_readerBanners, "reader.banners");
        root.Children.Add(_readerBanners);
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

        if (_reader is not null)
        {
            AttachBanners(_reader);
        }
    }

    // ReaderViewModel.Apply syncs Banners in the reveal's UI turn, so the stack changes with the page
    private void AttachBanners(ReaderViewModel reader)
    {
        void OnBanners(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => SyncBanners();
        reader.Banners.CollectionChanged += OnBanners;
        // R2-13: Unloaded can arrive after the next Loaded (the view moved in the tree): only a view that is really gone lets go
        Unloaded += (_, _) =>
        {
            if (!IsLoaded)
            {
                reader.Banners.CollectionChanged -= OnBanners;
            }
        };
        Loaded += (_, _) =>
        {
            reader.Banners.CollectionChanged -= OnBanners;
            reader.Banners.CollectionChanged += OnBanners;
            SyncBanners();
        };
        SyncBanners();
    }

    private void SyncBanners()
    {
        _readerBanners.Children.Clear();
        foreach (var banner in _reader!.Banners)
        {
            _readerBanners.Children.Add(ReaderBannerView(banner));
        }

        _readerBanners.Visibility = _readerBanners.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border ReaderBannerView(ReaderBanner banner)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = banner.Title, FontSize = 12.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = Brush(banner.IsWarning ? "Accent" : "Text") });
        if (banner.Detail is { Length: > 0 } detail)
        {
            text.Children.Add(new TextBlock { Text = detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brush("Muted") });
        }

        if (banner.Note is { Length: > 0 } note)
        {
            text.Children.Add(new TextBlock { Text = note, FontSize = 11.5, Foreground = Brush("Muted") });
        }

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(text);
        var emailId = _reader!.EmailId;
        foreach (var (actionId, label) in banner.Actions)
        {
            var button = ActionButton(label, "reader.banner." + actionId, () => _ = RunAsync(emailId, actionId, banner));
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(button, grid.ColumnDefinitions.Count - 1);
            grid.Children.Add(button);
        }

        var border = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 6, 6, 6), Background = Brush("Tile2"), Child = grid };
        AutomationProperties.SetAutomationId(border, "reader.banner-" + banner.Id);
        AutomationProperties.SetName(border, banner.Title);
        return border;
    }

    private async Task RunAsync(string emailId, string actionId, ReaderBanner banner)
    {
        if (string.Equals(actionId, VerificationCodeBannerSource.Copy, StringComparison.Ordinal))
        {
            // the code is the last word of the chip's title
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(banner.Title[(banner.Title.LastIndexOf(' ') + 1)..]);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            return;
        }

        if (_actions is not null)
        {
            await _actions.RunAsync(emailId, actionId, CancellationToken.None).ConfigureAwait(true);
        }
    }

    private async Task TrustSenderAsync()
    {
        if (_trusted is null || _reader?.Current.Body is not { From: [var from, ..] })
        {
            return;
        }

        var r = await _trusted.AddAsync(from.Email, CancellationToken.None).ConfigureAwait(true);
        if (r.IsOk)
        {
            _shell?.LoadExternalContent();
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
        // the shell outlives every view (it is one warm WebView2): a view that left the tree must not stay subscribed to it
        void OnState(object? sender, EventArgs e) => Sync(shell);
        shell.StateChanged += OnState;
        Unloaded += (_, _) =>
        {
            if (!IsLoaded)
            {
                shell.StateChanged -= OnState;
            }
        };
        Loaded += (_, _) =>
        {
            shell.StateChanged -= OnState;
            shell.StateChanged += OnState;
            Sync(shell);
        };
        // a closed and re-created WebView (background release, D-WV-01) goes into the same place
        shell.ViewReplaced += (_, _) =>
        {
            var fresh = shell.View;
            if (fresh.Parent is Panel p && !ReferenceEquals(p, _host))
            {
                p.Children.Remove(fresh);
            }

            if (!_host.Children.Contains(fresh))
            {
                _host.Children.Insert(0, fresh);
            }
        };
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
        var button = ActionButton(action, automationId, click);
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

    private static Button ActionButton(string action, string automationId, Action click)
    {
        var button = new Button { Content = new TextBlock { Text = action }, Height = 30, Padding = new Thickness(12, 0, 12, 0) };
        if (Application.Current.Resources.TryGetValue("Button.Action", out var style) && style is Style s)
        {
            button.Style = s;
        }

        button.Click += (_, _) => click();
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, action);
        return button;
    }

    private static SolidColorBrush Brush(string key) => Application.Current.Resources.ContainsKey(key) ? AppResources.Brush(key) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
}
