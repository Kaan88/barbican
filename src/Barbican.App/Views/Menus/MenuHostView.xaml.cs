// Mirrors: spec/menus.md conventions; Rust ctxmenu.rs row_h :94 (30 px rows, 9 px separators, 24 px headers), fill :98, show :107, hover :134; design/MOTION.md PopIn (from the anchor
// corner, Motion.enter, nothing waits); Bulwark lib/menu-placement.ts (the menu stays inside the window; a flyout opens beside its row or flips to the other side). ADR 0005 and 0007:
// one constrained in-window Popup. The model is ContextMenuController (Presentation); this only draws it and forwards pointer input. Owner menus.

using Barbican.App.Controls;
using Barbican.App.Motion;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Menus;
using Windows.Foundation;

namespace Barbican.App.Views.Menus;

/// <summary>Draws the open context menu and its flyout.</summary>
public sealed partial class MenuHostView : UserControl
{
    private const double CardWidth = 232;
    private const double FlyoutWidth = 216;
    private const double CardPadding = 4;
    private const double EdgeMargin = 8;

    private readonly ContextMenuController _menu = ViewHost.Get<ContextMenuController>();
    private readonly List<Border> _rootRows = [];
    private readonly List<Border> _flyRows = [];
    private MenuLevel? _rootLevel;
    private MenuLevel? _flyLevel;
    private Border? _rootCard;
    private Border? _flyCard;
    private bool _attached;

    public MenuHostView()
    {
        InitializeComponent();
        _menu.Changed += (_, _) => Render();
        Loaded += (_, _) =>
        {
            if (!_attached && XamlRoot?.Content is UIElement root)
            {
                _attached = true;
                ViewHost.Get<ContextPointerRouter>().Attach(root, this);
            }
        };
        Backdrop.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, Backdrop) || ReferenceEquals(e.OriginalSource, Layer))
            {
                _menu.Close();
            }
        };
        Backdrop.RightTapped += (_, e) =>
        {
            e.Handled = true;
            _menu.Close();
        };
    }

    // MA0051: one pass over the two cards (root, then flyout); splitting it would scatter the order the cards are replaced in (nothing blanks while the new one is built).
#pragma warning disable MA0051
    private void Render()
    {
        if (!_menu.IsOpen)
        {
            MenuPopup.IsOpen = false;
            Layer.Children.Clear();
            _rootLevel = null;
            _flyLevel = null;
            _rootCard = null;
            _flyCard = null;
            return;
        }

        Backdrop.Width = Math.Max(ActualWidth, 1);
        Backdrop.Height = Math.Max(ActualHeight, 1);
        if (!ReferenceEquals(_menu.Root, _rootLevel))
        {
            Layer.Children.Clear();
            _flyLevel = null;
            _flyCard = null;
            _rootLevel = _menu.Root;
            _rootCard = BuildCard(_rootLevel!, CardWidth, _rootRows, inFlyout: false);
            var (x, y, flippedX, flippedY) = Place(Local(_menu.Anchor), _rootLevel!.Height + 2 * CardPadding + 2, CardWidth);
            Canvas.SetLeft(_rootCard, x);
            Canvas.SetTop(_rootCard, y);
            Layer.Children.Add(_rootCard);
            MenuPopup.IsOpen = true;
            PopIn.Run(_rootCard, new Point(flippedX ? 1 : 0, flippedY ? 1 : 0));
        }

        if (!ReferenceEquals(_menu.Flyout, _flyLevel))
        {
            if (_flyCard is not null)
            {
                Layer.Children.Remove(_flyCard);
            }

            _flyLevel = _menu.Flyout;
            _flyCard = null;
            if (_flyLevel is not null && _rootCard is not null)
            {
                _flyCard = BuildCard(_flyLevel, FlyoutWidth, _flyRows, inFlyout: true);
                var rootLeft = Canvas.GetLeft(_rootCard);
                var rowTop = Canvas.GetTop(_rootCard) + CardPadding + 1 + _rootLevel!.TopOf(_flyLevel.ParentIndex);
                var height = _flyLevel.Height + 2 * CardPadding + 2;
                var width = Math.Max(Backdrop.Width, 1);
                var right = rootLeft + CardWidth - 4;
                var flip = right + FlyoutWidth + EdgeMargin > width;
                var x = flip ? rootLeft - FlyoutWidth + 4 : right;
                var y = Math.Clamp(rowTop - CardPadding, EdgeMargin, Math.Max(EdgeMargin, Backdrop.Height - height - EdgeMargin));
                Canvas.SetLeft(_flyCard, Math.Max(EdgeMargin, x));
                Canvas.SetTop(_flyCard, y);
                Layer.Children.Add(_flyCard);
                PopIn.Run(_flyCard, new Point(flip ? 1 : 0, 0));
            }
        }

        Highlight(_rootLevel!, _rootRows);
        if (_flyLevel is not null)
        {
            Highlight(_flyLevel, _flyRows);
        }
    }

#pragma warning restore MA0051

    // Bulwark lib/menu-placement.ts: the corner is the anchor; the card moves inside the window when it would leave it (flipping to the other side of the anchor first).
    private (double X, double Y, bool FlippedX, bool FlippedY) Place(Point anchor, double height, double width)
    {
        var w = Math.Max(Backdrop.Width, 1);
        var h = Math.Max(Backdrop.Height, 1);
        var flipX = anchor.X + width + EdgeMargin > w;
        var flipY = anchor.Y + height + EdgeMargin > h;
        var x = flipX ? anchor.X - width : anchor.X;
        var y = flipY ? anchor.Y - height : anchor.Y;
        return (Math.Clamp(x, EdgeMargin, Math.Max(EdgeMargin, w - width - EdgeMargin)), Math.Clamp(y, EdgeMargin, Math.Max(EdgeMargin, h - height - EdgeMargin)), flipX, flipY);
    }

    // anchors are in window coordinates (the pointer's position in the shell page); the popup's origin is the top-left of this view
    private Point Local(MenuAnchor anchor)
    {
        if (XamlRoot?.Content is UIElement root)
        {
            var origin = TransformToVisual(root).TransformPoint(new Point(0, 0));
            return new Point(anchor.X - origin.X, anchor.Y - origin.Y);
        }

        return new Point(anchor.X, anchor.Y);
    }

    private Border BuildCard(MenuLevel level, double width, List<Border> rows, bool inFlyout)
    {
        rows.Clear();
        var stack = new StackPanel();
        for (var i = 0; i < level.Items.Count; i++)
        {
            var row = BuildRow(level.Items[i], i, inFlyout);
            rows.Add(row);
            stack.Children.Add(row);
        }

        var card = new Border
        {
            Width = width,
            Padding = new Thickness(CardPadding),
            Background = AppResources.Brush("Popup"),
            BorderBrush = AppResources.Brush("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = stack,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(card, inFlyout ? "menu.flyout" : "menu.card");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(card, "Menu");
        return card;
    }

    private Border BuildRow(MenuItemSpec item, int index, bool inFlyout)
    {
        var row = new Border { Height = MenuLevel.HeightOf(item), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0)) };
        switch (item.Kind)
        {
            case MenuItemKind.Separator:
                row.Child = new Border { Height = 1, Background = AppResources.Brush("Line"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
                return row;
            case MenuItemKind.Header:
                row.Child = new TextBlock { Text = item.Label, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AppResources.Brush("Faint"), Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                return row;
            default:
                break;
        }

        var foreground = !item.Enabled ? AppResources.Brush("Faint") : item.Danger ? AppResources.Brush("RoseFg") : AppResources.Brush("Text");
        var grid = new Grid { Margin = new Thickness(8 + item.Indent * 12, 0, 8, 0), ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Icon(item, foreground));
        var label = new TextBlock { Text = item.Label, FontSize = 13, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        if (item.Checked == true)
        {
            var check = new SIcon { Kind = "Check", Size = 13, Foreground = AppResources.Brush("Accent"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(check, 2);
            grid.Children.Add(check);
        }
        else if (!string.IsNullOrEmpty(item.Hint))
        {
            var hint = new TextBlock { Text = item.Hint, FontSize = 11, Foreground = AppResources.Brush("Faint"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(hint, 2);
            grid.Children.Add(hint);
        }

        if (item.Kind == MenuItemKind.Submenu)
        {
            var chevron = new SIcon { Kind = "ChevronRight", Size = 12, Foreground = AppResources.Brush("Faint"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(chevron, 3);
            grid.Children.Add(chevron);
        }

        row.Child = grid;
        row.PointerEntered += (_, _) => _menu.Hover(index, inFlyout);
        row.Tapped += (_, e) =>
        {
            e.Handled = true;
            _ = _menu.ActivateAsync(index, inFlyout);
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(row, (inFlyout ? "menu.flyout." : "menu.item.") + item.Label.Trim());
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, item.Label.Trim());
        return row;
    }

    private static UIElement Icon(MenuItemSpec item, Brush foreground)
    {
        if (item.Icon is { } icon && icon.StartsWith("dot:", StringComparison.Ordinal) && uint.TryParse(icon.AsSpan(4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb))
        {
            return new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)),
            };
        }

        return new SIcon { Kind = item.Icon ?? string.Empty, Size = 15, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center };
    }

    private static void Highlight(MenuLevel level, List<Border> rows)
    {
        for (var i = 0; i < rows.Count && i < level.Items.Count; i++)
        {
            rows[i].Background = i == level.Highlight ? AppResources.Brush("Hover") : new SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }
    }
}
