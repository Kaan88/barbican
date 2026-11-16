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
    readonly List<Grid> folderGrids = new();
    readonly List<(Grid row, TextBlock name, Border pill)> labelRows = new();
    DataTemplate bentoRow;
    bool suppressFolder;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1560, 960));

        MailList.SelectionChanged += (_, _) => { if (MailList.SelectedItem is Mail m) Open(m); };
        bentoRow = MailList.ItemTemplate;
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
        bool n = Palette.Nordic;
        double size = n ? 46 : 44;
        var b = new Button
        {
            Width = size, Height = size, Padding = new Thickness(0), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(n ? 23 : 14),
            Background = on ? Palette.B("Sel") : Palette.B("Clear"),
            Content = SIcon.Make(icon, on ? "SelText" : "Faint", n ? 21 : 20),
        };
        ToolTipService.SetToolTip(b, tip);
        if (click != null) b.Click += click;
        railButtons.Add(b);
        return b;
    }

    void BuildRail()
    {
        bool n = Palette.Nordic;
        RailTop.Children.Clear(); RailBottom.Children.Clear(); railButtons.Clear();
        RailTop.Spacing = n ? 12 : 8;
        RailBottom.Spacing = n ? 12 : 8;
        if (n)
        {
            // Nordic mark: three rounded bars (NordMail.dc.html)
            var c = new Canvas { Width = 34, Height = 34, Margin = new Thickness(0, 0, 0, 14) };
            foreach (var (x, y, h, o) in new[] { (3.0, 12.0, 19.0, 1.0), (13.5, 3.0, 28.0, 0.7), (24.0, 8.0, 23.0, 0.4) })
            {
                var r = new Rectangle { Width = 7, Height = h, RadiusX = 3.5, RadiusY = 3.5, Fill = Palette.B("Accent"), Opacity = o };
                Canvas.SetLeft(r, x); Canvas.SetTop(r, y);
                c.Children.Add(r);
            }
            RailTop.Children.Add(c);
        }
        else
        {
            RailTop.Children.Add(new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(13), Background = Palette.B("Accent"), Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock { Text = "B", FontWeight = Microsoft.UI.Text.FontWeights.Black, FontSize = 18, Foreground = Palette.B("OnAccent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
        }
        foreach (var (icon, tip, on) in new[] { ("mail", "Mail", true), ("cal", "Calendar (stub)", false), ("users", "Contacts (stub)", false), ("folder", "Files (stub)", false), ("spark", "Assistant (stub)", false), ("shield", "Admin (stub)", false) })
            RailTop.Children.Add(RailButton(icon, tip, on));
        RailBottom.Children.Add(RailButton("grid", "Switch Bento / Nordic (S)", false, (_, _) => ToggleStyle()));
        RailBottom.Children.Add(RailButton("sun", "Switch dark / light (T)", false, (_, _) => ToggleTheme()));
        if (n)
            RailBottom.Children.Add(new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Palette.B("Sel"), Margin = new Thickness(0, 4, 0, 0),
                Child = new TextBlock { Text = "KK", FontWeight = Microsoft.UI.Text.FontWeights.Bold, FontSize = 13, Foreground = Palette.B("SelText"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
    }

    void BuildFolders()
    {
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
            folderGrids.Add(g);
            FolderList.Items.Add(g);
        }
        FolderList.SelectionChanged += (_, _) =>
        {
            if (!suppressFolder && FolderList.SelectedItem is Grid g) ShowFolder((string)g.Tag);
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
            labelRows.Add((g, tb, pill));
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
            var nb = new Button
            {
                Content = label, Height = 38, Padding = new Thickness(16, 0, 16, 0), CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(1),
                FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            };
            if (label == "Archive") nb.Click += (_, _) => MoveCurrent("Archive");
            NordActions.Children.Add(nb);
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
            r.name.FontWeight = Palette.Nordic ? (on ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Medium)
                                               : (on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal);
            r.icon.Stroke = Palette.B(on ? "SelText" : "Dim");
            r.count.Foreground = Palette.B(on ? "SelText" : "Faint");
        }
        FolderTitle.Text = name;
        var items = Sample.All.Where(m => m.Folder == name).ToList();
        int unread = items.Count(m => m.Unread);
        UnreadText.Text = unread > 0 ? $"{unread} unread" : $"{items.Count} messages";
        Subtitle.Text = name == "Inbox" ? $"Good morning. {unread} new since yesterday." : $"{items.Count} messages";
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
        NordSubject.Text = m.Subject;
        NordMeta.Text = m.From == "Me" ? $"You · {m.Time}" : $"{m.From} to you · {m.Time}";
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
        if (Palette.Nordic) { BuildNordContext(m); return; }
        var p = ContextPanel;
        p.Padding = new Thickness(22, 26, 22, 26);
        p.Spacing = 10;
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

    // NordMail.dc.html aside: plain text, separated by hairlines, no cards.
    void BuildNordContext(Mail m)
    {
        var p = ContextPanel;
        p.Children.Clear();
        p.Padding = new Thickness(22, 30, 22, 30);
        p.Spacing = 16;
        TextBlock T(string text, double size, string brush, Windows.UI.Text.FontWeight? w = null) =>
            new() { Text = text, FontSize = size, Foreground = Palette.B(brush), FontWeight = w ?? Microsoft.UI.Text.FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
        Border Hair() => new() { Height = 1, Background = Palette.B("Line") };
        p.Children.Add(new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Background = m.AvatarBg, HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = m.Initials, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold, Foreground = m.AvatarFg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var who = new StackPanel();
        who.Children.Add(T(m.From, 17, "Text", Microsoft.UI.Text.FontWeights.ExtraBold));
        if (!string.IsNullOrEmpty(m.Org)) who.Children.Add(T(m.Org, 13, "Dim"));
        p.Children.Add(who);
        var contact = new StackPanel { Spacing = 8 };
        contact.Children.Add(T(m.Email, 13, "Body"));
        if (m.Label != null) contact.Children.Add(T("Label: " + m.Label, 13, "Body"));
        p.Children.Add(contact);
        p.Children.Add(Hair());
        p.Children.Add(T("Recent", 12, "Dim", Microsoft.UI.Text.FontWeights.Bold));
        var rec = new StackPanel { Spacing = 10 };
        foreach (var r in Sample.All.Where(x => x.Email == m.Email)) rec.Children.Add(T($"{r.Subject} · {r.Time}", 13, "Body"));
        p.Children.Add(rec);
        if (m.From == "Mira Okafor")
        {
            p.Children.Add(Hair());
            p.Children.Add(T("Coming up", 12, "Dim", Microsoft.UI.Text.FontWeights.Bold));
            var tb = new TextBlock { FontSize = 13, Foreground = Palette.B("Body") };
            tb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Thu 14:00 ", Foreground = Palette.B("AmberFg"), FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            tb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Design review" });
            p.Children.Add(tb);
        }
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
        var res = Application.Current.Resources;

        // shell: Nordic has rail + folders straight on the ground, one surface inset 14px
        Shell.Padding = n ? new Thickness(0, 0, 14, 14) : new Thickness(14, 0, 14, 14);
        Shell.ColumnSpacing = n ? 0 : 12;
        FolderCol.Width = new GridLength(n ? 228 : 236);
        foreach (var b in new[] { RailTile, FolderTile })
        {
            b.Background = n ? clear : tile;
            b.BorderThickness = new Thickness(n ? 0 : 1);
        }
        RailTile.Width = n ? 76 : 66;
        RailTile.Padding = n ? new Thickness(0, 14, 0, 22) : new Thickness(0, 14, 0, 14);
        FolderTile.Padding = n ? new Thickness(14, 18, 14, 22) : new Thickness(12, 14, 12, 14);
        BuildRail();

        // folders
        ComposeBtn.Height = n ? 50 : 46;
        ComposeBtn.CornerRadius = new CornerRadius(n ? 25 : 16);
        ComposeBtn.Margin = new Thickness(0, 0, 0, n ? 22 : 12);
        ComposeIcon.Visibility = n ? Visibility.Collapsed : Visibility.Visible;
        ComposeText.FontSize = n ? 14.5 : 14;
        foreach (var r in folderRows.Values)
        {
            r.icon.Visibility = n ? Visibility.Collapsed : Visibility.Visible;
            r.name.FontSize = n ? 14.5 : 13.5;
            r.count.FontSize = n ? 12.5 : 12;
            r.count.FontWeight = n ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.SemiBold;
        }
        int fi = Math.Max(FolderList.SelectedIndex, 0);
        suppressFolder = true;
        FolderList.Items.Clear();
        FolderList.ItemContainerStyle = (Style)res[n ? "NordFolderItem" : "FolderItem"];
        foreach (var g in folderGrids) FolderList.Items.Add(g);
        FolderList.SelectedIndex = fi;
        suppressFolder = false;
        LabelsHead.Text = n ? "Labels" : "LABELS";
        LabelsHead.FontSize = n ? 12 : 11;
        LabelsHead.CharacterSpacing = n ? 0 : 80;
        LabelsHead.Foreground = Palette.B(n ? "Dim" : "Faint");
        LabelsHead.Margin = n ? new Thickness(16, 24, 0, 8) : new Thickness(11, 18, 0, 6);
        LabelPanel.Spacing = n ? 0 : 2;
        foreach (var l in labelRows)
        {
            l.row.Padding = n ? new Thickness(16, 9, 16, 9) : new Thickness(11, 7, 11, 7);
            l.row.ColumnSpacing = n ? 12 : 10;
            l.name.FontSize = n ? 14 : 13.5;
            l.pill.Visibility = n ? Visibility.Collapsed : Visibility.Visible;
        }
        StorageText.Visibility = n ? Visibility.Visible : Visibility.Collapsed;

        // one continuous surface split by hairlines (Nordic) vs separate tiles (Bento)
        Surface.Background = n ? tile : clear;
        Surface.BorderBrush = line;
        Surface.BorderThickness = new Thickness(n ? 1 : 0);
        Surface.CornerRadius = new CornerRadius(n ? 28 : 0);
        SurfaceGrid.ColumnSpacing = n ? 0 : 12;
        ListCol.Width = new GridLength(n ? 390 : 370);
        CtxCol.Width = new GridLength(n ? 250 : 272);
        foreach (var b in new[] { ListTile, ReaderTile, ContextTile })
        {
            b.Background = n ? clear : tile;
            b.CornerRadius = new CornerRadius(n ? 0 : 26);
            b.BorderThickness = new Thickness(n ? 0 : 1);
        }
        if (n)
        {
            ListTile.BorderThickness = new Thickness(0, 0, 1, 0);
            ContextTile.BorderThickness = new Thickness(1, 0, 0, 0);
            ListTile.CornerRadius = new CornerRadius(27, 0, 0, 27);
            ContextTile.CornerRadius = new CornerRadius(0, 27, 27, 0);
        }

        // list header + rows
        ListHead.Padding = n ? new Thickness(28, 28, 28, 16) : new Thickness(20, 18, 20, 10);
        FolderTitle.FontSize = n ? 28 : 24;
        FolderTitle.FontWeight = n ? Microsoft.UI.Text.FontWeights.ExtraBold : Microsoft.UI.Text.FontWeights.Bold;
        FolderTitle.CharacterSpacing = n ? -20 : 0;
        UnreadText.Visibility = n ? Visibility.Collapsed : Visibility.Visible;
        Subtitle.Visibility = n ? Visibility.Visible : Visibility.Collapsed;
        Chips.Visibility = n ? Visibility.Collapsed : Visibility.Visible;
        int si = MailList.SelectedIndex;
        var src = MailList.ItemsSource;
        MailList.ItemsSource = null;
        MailList.ItemTemplate = n ? (DataTemplate)Root.Resources["NordRow"] : bentoRow;
        MailList.ItemContainerStyle = (Style)res[n ? "NordRowItem" : "RowItem"];
        MailList.Padding = n ? new Thickness(0) : new Thickness(0, 0, 0, 8);
        MailList.ItemsSource = src;
        if (si >= 0) MailList.SelectedIndex = si;

        // reader
        ReaderHead.Visibility = n ? Visibility.Collapsed : Visibility.Visible;
        NordHead.Visibility = n ? Visibility.Visible : Visibility.Collapsed;
        QuickReply.Visibility = n ? Visibility.Visible : Visibility.Collapsed;
        BodyHost.Margin = n ? new Thickness(0, 18, 0, 12) : new Thickness(8, 0, 8, 10);
        BodyHost.CornerRadius = new CornerRadius(n ? 0 : 18);
        HeadAvatar.CornerRadius = new CornerRadius(n ? 20 : 13);
        if (MailList.ItemsSource is List<Mail> ms) foreach (var m in ms) m.Refresh();
        if (current != null) { BuildContext(current); _ = ShowBody(current); }

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
