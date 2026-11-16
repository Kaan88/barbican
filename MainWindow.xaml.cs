using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using Windows.System;

namespace Barbican;

public sealed partial class MainWindow : Window
{
    string folder = "Inbox";
    Mail current;
    Mail menuTarget;
    WebView2 front, back;
    int seq;
    readonly Dictionary<string, (TextBlock name, SIcon icon, TextBlock count)> folderRows = new();
    readonly List<Button> railButtons = new();

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1560, 960));

        MailList.SelectionChanged += (_, _) => { if (MailList.SelectedItem is Mail m) Open(m); };
        BuildRail();
        BuildFolders();
        BuildLabels();
        BuildChips();
        BuildActions();
        MailList.RightTapped += MailList_RightTapped;
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Root_KeyDown), true);

        var args = Environment.GetCommandLineArgs();
        if (args.Contains("light")) Palette.Dark = false;
        if (args.Contains("nordic")) Palette.Nordic = true;
        ApplyTheme();
        ApplyStyle(false);
        _ = InitWebAsync();
    }

    // ---------- chrome ----------

    Button RailButton(string icon, string tip, bool on, RoutedEventHandler click = null)
    {
        var b = new Button
        {
            Width = 44, Height = 44, Padding = new Thickness(0), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(14),
            Background = on ? Palette.B("Sel") : Palette.B("Clear"),
            Content = SIcon.Make(icon, on ? "SelText" : "Faint", 20),
        };
        ToolTipService.SetToolTip(b, tip);
        if (click != null) b.Click += click;
        railButtons.Add(b);
        return b;
    }

    void BuildRail()
    {
        RailTop.Children.Add(new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(13), Background = Palette.B("Accent"), Margin = new Thickness(0, 0, 0, 10),
            Child = new TextBlock { Text = "B", FontWeight = Microsoft.UI.Text.FontWeights.Black, FontSize = 18, Foreground = Palette.B("OnAccent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        foreach (var (icon, tip, on) in new[] { ("mail", "Mail", true), ("cal", "Calendar (stub)", false), ("users", "Contacts (stub)", false), ("folder", "Files (stub)", false), ("spark", "Assistant (stub)", false), ("shield", "Admin (stub)", false) })
            RailTop.Children.Add(RailButton(icon, tip, on));
        RailBottom.Children.Add(RailButton("grid", "Switch Bento / Nordic (S)", false, (_, _) => ToggleStyle()));
        RailBottom.Children.Add(RailButton("sun", "Switch dark / light (T)", false, (_, _) => ToggleTheme()));
    }

    void BuildFolders()
    {
        FolderList.Resources["ListViewItemBackgroundSelected"] = Palette.B("Sel");
        FolderList.Resources["ListViewItemBackgroundSelectedPointerOver"] = Palette.B("Sel");
        FolderList.Resources["ListViewItemBackgroundSelectedPressed"] = Palette.B("Sel");
        FolderList.Resources["ListViewItemSelectionIndicatorBrush"] = Palette.B("Clear");
        foreach (var (name, icon) in Sample.Folders)
        {
            var g = new Grid { ColumnSpacing = 10, Tag = name };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = SIcon.Make(icon, "Dim");
            var tb = new TextBlock { Text = name, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.B("Body") };
            int n = Sample.Count(name);
            var ct = new TextBlock { Text = n > 0 ? n.ToString() : "", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.B("Faint") };
            Grid.SetColumn(tb, 1); Grid.SetColumn(ct, 2);
            g.Children.Add(ic); g.Children.Add(tb); g.Children.Add(ct);
            folderRows[name] = (tb, ic, ct);
            FolderList.Items.Add(g);
        }
        FolderList.SelectionChanged += (_, _) =>
        {
            if (FolderList.SelectedItem is Grid g) ShowFolder((string)g.Tag);
        };
        FolderList.SelectedIndex = 0;
    }

    void BuildLabels()
    {
        foreach (var (name, n, tint) in new[] { ("Work", "18", "Rose"), ("Family", "4", "Amber"), ("Receipts", "31", "Mint"), ("Travel", "2", "Sky") })
        {
            var g = new Grid { ColumnSpacing = 10, Padding = new Thickness(11, 7, 11, 7) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(4), Background = Palette.B(tint + "Fg"), VerticalAlignment = VerticalAlignment.Center });
            var tb = new TextBlock { Text = name, FontSize = 13.5, Foreground = Palette.B("Body") };
            var pill = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 7, 2), Background = Palette.B(tint + "Bg"), Child = new TextBlock { Text = n, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = Palette.B(tint + "Fg") } };
            Grid.SetColumn(tb, 1); Grid.SetColumn(pill, 2);
            g.Children.Add(tb); g.Children.Add(pill);
            LabelPanel.Children.Add(g);
        }
    }

    void BuildChips()
    {
        bool first = true;
        foreach (var c in new[] { "Focused", "Other", "Newsletters" })
        {
            var b = new Button
            {
                Content = c, Height = 30, Padding = new Thickness(13, 0, 13, 0), CornerRadius = new CornerRadius(15), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Background = first ? Palette.B("Text") : Palette.B("Clear"), Foreground = first ? Palette.B("Ground") : Palette.B("Dim"),
                BorderThickness = new Thickness(first ? 0 : 1),
            };
            if (first)
            {
                b.Resources["ButtonBackgroundPointerOver"] = Palette.B("Text");
                b.Resources["ButtonForegroundPointerOver"] = Palette.B("Ground");
                b.Resources["ButtonBackgroundPressed"] = Palette.B("Body");
                b.Resources["ButtonForegroundPressed"] = Palette.B("Ground");
            }
            first = false;
            Chips.Children.Add(b);
        }
    }

    void BuildActions()
    {
        foreach (var (label, icon) in new[] { ("Reply", "reply"), ("Forward", "fwd"), ("Archive", "archive"), ("Snooze", "clock") })
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            sp.Children.Add(SIcon.Make(icon, "Body", 16));
            sp.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            var b = new Button { Content = sp, Height = 36, Padding = new Thickness(14, 0, 16, 0), CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1) };
            if (label == "Archive") b.Click += (_, _) => MoveCurrent("Archive");
            Actions.Children.Add(b);
        }
    }

    // ---------- folder / list ----------

    void ShowFolder(string name)
    {
        folder = name;
        foreach (var (k, r) in folderRows)
        {
            bool on = k == name;
            r.name.Foreground = Palette.B(on ? "SelText" : "Body");
            r.name.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            r.icon.Stroke = Palette.B(on ? "SelText" : "Dim");
            r.count.Foreground = Palette.B(on ? "SelText" : "Faint");
        }
        FolderTitle.Text = name;
        var items = Sample.All.Where(m => m.Folder == name).ToList();
        int unread = items.Count(m => m.Unread);
        UnreadText.Text = unread > 0 ? $"{unread} unread" : $"{items.Count} messages";
        MailList.ItemsSource = items;
        if (items.Count > 0) MailList.SelectedIndex = 0;
    }

    void Open(Mail m)
    {
        current = m;
        if (m.Unread)
        {
            m.Unread = false;
            m.Refresh();
            RefreshCounts();
        }
        // re-add header children so the ContentThemeTransition plays (a small entrance)
        SubjectText.Text = m.Subject;
        HeadFrom.Text = m.From;
        HeadEmail.Text = m.Email;
        HeadTime.Text = m.Time;
        HeadInitials.Text = m.Initials;
        HeadInitials.Foreground = m.AvatarFg;
        HeadAvatar.Background = m.AvatarBg;
        BuildContext(m);
        _ = ShowBody(m);
    }

    void RefreshCounts()
    {
        foreach (var (k, r) in folderRows) { int n = Sample.Count(k); r.count.Text = n > 0 ? n.ToString() : ""; }
        var items = MailList.ItemsSource as List<Mail>;
        if (items != null) { int u = items.Count(x => x.Unread); UnreadText.Text = u > 0 ? $"{u} unread" : $"{items.Count} messages"; }
    }

    void BuildContext(Mail m)
    {
        var p = ContextPanel;
        p.Children.Clear();
        p.Children.Add(new Border
        {
            Width = 68, Height = 68, CornerRadius = new CornerRadius(Palette.Nordic ? 34 : 22), Background = m.AvatarBg, HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = m.Initials, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = m.AvatarFg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        p.Children.Add(new TextBlock { Text = m.From, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = Palette.B("Text"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(m.Org)) p.Children.Add(new TextBlock { Text = m.Org, FontSize = 13, Foreground = Palette.B("Dim"), TextWrapping = TextWrapping.Wrap });
        p.Children.Add(new TextBlock { Text = m.Email, FontSize = 12.5, Foreground = Palette.B("Faint") });
        if (m.Label != null)
        {
            var tint = m.Label switch { "Work" => "Rose", "Family" => "Amber", "Receipts" => "Mint", _ => "Sky" };
            p.Children.Add(new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 3, 10, 4), Background = Palette.B(tint + "Bg"), HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = m.Label, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Palette.B(tint + "Fg") } });
        }
        var acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 8) };
        foreach (var (icon, tip) in new[] { ("mail", "Write"), ("cal", "Schedule"), ("users", "Contact card") })
        {
            var b = new Button { Width = 40, Height = 40, Padding = new Thickness(0), CornerRadius = new CornerRadius(Palette.Nordic ? 20 : 13), BorderThickness = new Thickness(1), Content = SIcon.Make(icon, "Body", 17) };
            ToolTipService.SetToolTip(b, tip);
            acts.Children.Add(b);
        }
        p.Children.Add(acts);
        p.Children.Add(new TextBlock { Text = "RECENT", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold, CharacterSpacing = 80, Foreground = Palette.B("Faint"), Margin = new Thickness(0, 6, 0, 0) });
        var recent = Sample.All.Where(x => x.Email == m.Email).ToList();
        foreach (var r in recent)
        {
            var box = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 9, 12, 10), Background = Palette.B("Tile2"), BorderBrush = Palette.B("Line"), BorderThickness = new Thickness(1) };
            var sp = new StackPanel { Spacing = 2 };
            sp.Children.Add(new TextBlock { Text = r.Subject, FontSize = 13, Foreground = Palette.B("Body"), TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(new TextBlock { Text = $"{r.Folder} · {r.Time}", FontSize = 11.5, Foreground = Palette.B("Faint") });
            box.Child = sp;
            p.Children.Add(box);
        }
        p.Children.Add(new TextBlock { Text = "NOTE", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold, CharacterSpacing = 80, Foreground = Palette.B("Faint"), Margin = new Thickness(0, 10, 0, 0) });
        p.Children.Add(new TextBlock { Text = "Sender context is sample data in this prototype.", FontSize = 12.5, Foreground = Palette.B("Dim"), TextWrapping = TextWrapping.Wrap });
    }

    void MoveCurrent(string to) => Move(current, to);

    void Move(Mail m, string to)
    {
        if (m == null) return;
        int idx = MailList.SelectedIndex;
        m.Folder = to;
        var items = Sample.All.Where(x => x.Folder == folder).ToList();
        MailList.ItemsSource = items;
        RefreshCounts();
        if (items.Count > 0) MailList.SelectedIndex = Math.Min(Math.Max(idx, 0), items.Count - 1);
    }

    // ---------- body (WebView2 double buffer) ----------

    async System.Threading.Tasks.Task InitWebAsync()
    {
        foreach (var w in new[] { WebA, WebB })
        {
            w.DefaultBackgroundColor = Palette.C("Tile");
            await w.EnsureCoreWebView2Async();
            var s = w.CoreWebView2.Settings;
            s.AreDefaultContextMenusEnabled = false;
            s.AreDevToolsEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsZoomControlEnabled = false;
            var self = w;
            w.CoreWebView2.WebMessageReceived += (_, e) => OnWebMessage(self, e.TryGetWebMessageAsString());
            w.NavigationCompleted += Web_NavigationCompleted;
            w.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
        }
        front = WebA; back = WebB;
        if (current != null) await ShowBody(current);
    }

    async System.Threading.Tasks.Task ShowBody(Mail m)
    {
        if (front == null) return;
        int my = ++seq;
        back.DefaultBackgroundColor = Palette.C("Tile");
        back.Tag = my;
        back.NavigateToString(MailHtml.Wrap(m));
        await System.Threading.Tasks.Task.CompletedTask;
    }

    void Web_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (sender != back || sender.Tag is not int t || t != seq) return;
        var incoming = back; var outgoing = front;
        Canvas.SetZIndex(incoming, 1);
        Canvas.SetZIndex(outgoing, 0);
        var sb = new Storyboard();
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(110), EnableDependentAnimation = true };
        Storyboard.SetTarget(fade, incoming);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);
        sb.Completed += (_, _) => { incoming.Opacity = 1; outgoing.Opacity = 0; };
        sb.Begin();
        front = incoming; back = outgoing;
    }

    void OnWebMessage(WebView2 w, string msg)
    {
        try
        {
            using var doc = JsonDocument.Parse(msg);
            var r = doc.RootElement;
            switch (r.GetProperty("t").GetString())
            {
                case "ctx":
                    menuTarget = current;
                    BuildMenu().ShowAt(w, new FlyoutShowOptions { Position = new Point(r.GetProperty("x").GetDouble(), r.GetProperty("y").GetDouble()) });
                    break;
                case "key":
                    HandleKey(r.GetProperty("k").GetString());
                    break;
            }
        }
        catch { }
    }

    // ---------- context menu ----------

    MenuFlyout BuildMenu()
    {
        var f = new MenuFlyout();
        MenuFlyoutItem Item(string text, string glyph, string key, Action act = null)
        {
            var it = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, KeyboardAcceleratorTextOverride = key };
            if (act != null) it.Click += (_, _) => act();
            return it;
        }
        var m = menuTarget;
        f.Items.Add(Item("Reply", "", "R"));
        f.Items.Add(Item("Reply all", "", "Shift+R"));
        f.Items.Add(Item("Forward", "", "F"));
        f.Items.Add(new MenuFlyoutSeparator());
        f.Items.Add(Item(m != null && m.Unread ? "Mark as read" : "Mark as unread", "", "U", () => { if (m != null) { m.Unread = !m.Unread; m.Refresh(); RefreshCounts(); } }));
        f.Items.Add(Item("Flag", "", "Ins"));
        var move = new MenuFlyoutSubItem { Text = "Move to", Icon = new FontIcon { Glyph = "" } };
        foreach (var (name, _) in Sample.Folders.Where(x => x.Name != "Drafts" && x.Name != "Scheduled" && x.Name != "Sent"))
        {
            var n = name;
            var it = new MenuFlyoutItem { Text = n, IsEnabled = m != null && m.Folder != n };
            it.Click += (_, _) => Move(m, n);
            move.Items.Add(it);
        }
        f.Items.Add(move);
        var label = new MenuFlyoutSubItem { Text = "Label", Icon = new FontIcon { Glyph = "" } };
        foreach (var l in new[] { "Work", "Family", "Receipts", "Travel" }) label.Items.Add(new ToggleMenuFlyoutItem { Text = l, IsChecked = m?.Label == l });
        f.Items.Add(label);
        f.Items.Add(new MenuFlyoutSeparator());
        f.Items.Add(Item("Archive", "", "E", () => Move(m, "Archive")));
        f.Items.Add(Item("Delete", "", "Del", () => Move(m, "Trash")));
        return f;
    }

    void MailList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is Mail m)
        {
            menuTarget = m;
            BuildMenu().ShowAt(MailList, new FlyoutShowOptions { Position = e.GetPosition(MailList) });
            e.Handled = true;
        }
    }

    // ---------- keys ----------

    void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl) return;
        string k = e.Key switch { VirtualKey.J => "j", VirtualKey.K => "k", VirtualKey.T => "t", VirtualKey.S => "s", _ => null };
        if (k == null) return;
        HandleKey(k);
        e.Handled = true;
    }

    void HandleKey(string k)
    {
        switch (k)
        {
            case "j": Step(1); break;
            case "k": Step(-1); break;
            case "t": ToggleTheme(); break;
            case "s": ToggleStyle(); break;
        }
    }

    void Step(int d)
    {
        int n = MailList.Items.Count;
        if (n == 0) return;
        int i = Math.Clamp(MailList.SelectedIndex + d, 0, n - 1);
        MailList.SelectedIndex = i;
        MailList.ScrollIntoView(MailList.SelectedItem);
    }

    // ---------- theme / style ----------

    void ToggleTheme()
    {
        Palette.Dark = !Palette.Dark;
        ApplyTheme();
        if (current != null) _ = ShowBody(current);
    }

    void ApplyTheme()
    {
        Palette.Apply();
        Root.RequestedTheme = Palette.Dark ? ElementTheme.Dark : ElementTheme.Light;
        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = Palette.C("Dim");
        tb.ButtonHoverBackgroundColor = Palette.C("Hover");
        tb.ButtonHoverForegroundColor = Palette.C("Text");
        tb.ButtonPressedBackgroundColor = Palette.C("Pressed");
        tb.ButtonInactiveForegroundColor = Palette.C("Faint");
        if (WebA.CoreWebView2 != null) { WebA.DefaultBackgroundColor = Palette.C("Tile"); WebB.DefaultBackgroundColor = Palette.C("Tile"); }
    }

    void ToggleStyle()
    {
        Palette.Nordic = !Palette.Nordic;
        ApplyStyle(true);
    }

    void ApplyStyle(bool animate)
    {
        bool n = Palette.Nordic;
        var tile = Palette.B("Tile"); var line = Palette.B("Line"); var clear = Palette.B("Clear");
        Shell.ColumnSpacing = n ? 6 : 12;
        foreach (var b in new[] { RailTile, FolderTile })
        {
            b.Background = n ? clear : tile;
            b.BorderThickness = new Thickness(n ? 0 : 1);
        }
        RailTile.CornerRadius = new CornerRadius(24);
        FolderTile.CornerRadius = new CornerRadius(26);
        Surface.Background = n ? tile : clear;
        Surface.BorderBrush = line;
        Surface.BorderThickness = new Thickness(n ? 1 : 0);
        Surface.CornerRadius = new CornerRadius(n ? 28 : 0);
        SurfaceGrid.ColumnSpacing = n ? 0 : 12;
        foreach (var b in new[] { ListTile, ReaderTile, ContextTile })
        {
            b.Background = n ? clear : tile;
            b.CornerRadius = new CornerRadius(n ? 0 : 26);
            b.BorderThickness = new Thickness(n ? 0 : 1);
        }
        if (n) ReaderTile.BorderThickness = new Thickness(1, 0, 1, 0);
        ListTile.CornerRadius = n ? new CornerRadius(28, 0, 0, 28) : new CornerRadius(26);
        ContextTile.CornerRadius = n ? new CornerRadius(0, 28, 28, 0) : new CornerRadius(26);
        HeadAvatar.CornerRadius = new CornerRadius(n ? 20 : 13);
        ComposeBtn.CornerRadius = new CornerRadius(n ? 23 : 16);
        foreach (var b in railButtons) b.CornerRadius = new CornerRadius(n ? 22 : 14);
        foreach (var c in FolderList.Items) if (FolderList.ContainerFromItem(c) is ListViewItem li) li.CornerRadius = new CornerRadius(n ? 19 : 14);
        if (MailList.ItemsSource is List<Mail> ms) foreach (var m in ms) m.Refresh();
        if (current != null) BuildContext(current);

        if (animate)
        {
            var sb = new Storyboard();
            var a = new DoubleAnimation { From = 0.55, To = 1, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, Shell); Storyboard.SetTargetProperty(a, "Opacity");
            var y = new DoubleAnimation { From = 10, To = 0, Duration = TimeSpan.FromMilliseconds(260), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(y, ShellShift); Storyboard.SetTargetProperty(y, "Y");
            sb.Children.Add(a); sb.Children.Add(y);
            sb.Begin();
        }
    }
}
