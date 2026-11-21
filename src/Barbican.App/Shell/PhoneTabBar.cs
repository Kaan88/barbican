// Mirrors: Bulwark components/layout/navigation-rail.tsx:390-445 (the phone's bottom tab bar: a <nav> with a top border on the background colour, one equal-width item per app,
// 44 px minimum height, a 20 px icon over a 10 px medium label, the active item in the primary colour with a 16 x 2 bar under the icon, aria-current="page", the unread badge
// at the icon's top end: a red 16 px pill or a 10 px dot), mail-app.tsx:6432-6447 (shown only in the phone layout, with the list; not while a message or the composer is open).
// WP-10 (SH-11, RS-01).

using Barbican.App.Controls;
using Barbican.App.Theme;
using Barbican.Presentation.Shell;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Shapes;

namespace Barbican.App.Shell;

/// <summary>
/// The bottom tab bar of the phone layout (a window narrower than <see cref="ShellLayout.PhoneBelow"/>): Mail, Calendar, Contacts, Files and Settings, the apps the rail would show.
/// It only displays <see cref="RailViewModel"/> and forwards a press to <see cref="ShellViewModel.TryActivate"/>; nothing animates (the active colour changes at once).
/// </summary>
public sealed class PhoneTabBar : UserControl
{
    private static readonly (AppId App, string Label, string Icon, string Id)[] Items =
    [
        (AppId.Mail, "Mail", "Mail", "mail"),
        (AppId.Calendar, "Calendar", "Cal", "calendar"),
        (AppId.Contacts, "Contacts", "Users", "contacts"),
        (AppId.Files, "Files", "Folder", "files"),
        (AppId.Settings, "Settings", "Settings", "settings"),
    ];

    private readonly RailViewModel _rail = ViewHost.Get<RailViewModel>();
    private readonly ShellViewModel _shell = ViewHost.Get<ShellViewModel>();
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly Grid _row = new();

    public PhoneTabBar()
    {
        AutomationProperties.SetAutomationId(this, "shell.tabbar");
        AutomationProperties.SetName(this, "Main navigation");
        AutomationProperties.SetLandmarkType(this, AutomationLandmarkType.Navigation);
        Content = new Border
        {
            Background = AppResources.Brush("Bg"),
            BorderBrush = AppResources.Brush("Line"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = _row,
        };
        _rail.PropertyChanged += (_, _) => Rebuild();
        _look.PropertyChanged += (_, _) => Rebuild();
        Rebuild();
    }

    /// <summary>Rebuilds the items: the apps that exist, the active one, the badge. Cheap (five items), and called only when the rail's state changes.</summary>
    public void Rebuild()
    {
        _row.Children.Clear();
        _row.ColumnDefinitions.Clear();
        var column = 0;
        foreach (var (app, label, icon, id) in Items)
        {
            if (!_rail.IsAvailable(app))
            {
                continue;
            }

            _row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var item = Item(app, label, icon, id);
            Grid.SetColumn(item, column++);
            _row.Children.Add(item);
        }
    }

    private Button Item(AppId app, string label, string icon, string id)
    {
        var active = _shell.ActiveApp == app;
        var foreground = AppResources.Brush(active ? "Accent" : "Dim");
        var glyph = new SIcon { Kind = icon, Size = 20, Foreground = foreground, HorizontalAlignment = HorizontalAlignment.Center };
        var iconBox = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        iconBox.Children.Add(glyph);
        if (app == AppId.Mail)
        {
            AddBadge(iconBox, _rail.MailBadge);
        }

        if (active)
        {
            iconBox.Children.Add(new Rectangle
            {
                Width = 16,
                Height = 2,
                RadiusX = 1,
                RadiusY = 1,
                Fill = AppResources.Brush("Accent"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -4),
                IsHitTestVisible = false,
            });
        }

        var text = new TextBlock
        {
            Text = label,
            FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var stack = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(iconBox);
        stack.Children.Add(text);
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["Button.Icon"],
            Content = stack,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(2, 8, 2, 8),
            MinHeight = Math.Max(ShellLayout.TabBarItemHeight, _look.MinimumTarget),
            CornerRadius = new CornerRadius(0),
        };
        AutomationProperties.SetAutomationId(button, "tabbar." + id);
        AutomationProperties.SetName(button, label);
        // aria-current="page" (navigation-rail.tsx:425): the active tab says so
        AutomationProperties.SetHelpText(button, active ? "Current page" : string.Empty);
        button.Click += (_, _) => _ = _shell.TryActivate(app);
        return button;
    }

    // the badge as the rail shows it (RailViewModel.MailBadge: "" hidden, "●" the dot style, else the count): navigation-rail.tsx:411-419
    private static void AddBadge(Grid host, string badge)
    {
        if (string.IsNullOrEmpty(badge))
        {
            return;
        }

        var red = StyleState.Current.RailBadgeBrush;
        if (string.Equals(badge, "●", StringComparison.Ordinal))
        {
            host.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = StyleState.Current.RailDotBrush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -2, -4, 0), IsHitTestVisible = false });
            return;
        }

        host.Children.Add(new Border
        {
            Background = red,
            CornerRadius = new CornerRadius(8),
            MinWidth = 16,
            Height = 16,
            Padding = new Thickness(4, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -6, -10, 0),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = badge,
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = StyleState.Current.RailBadgeForeground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
    }
}
