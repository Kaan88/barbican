// Bulwark 1.21.2 reference (default theme): components/keyboard-shortcuts-modal.tsx:92-93 (title text-lg with a muted keyboard icon in a Dialog: card rounded-lg shadow-xl p-6, scrim black/50),
// :106-130 (tab list with a border-bottom, the active tab underlined), :141 (section heading text-sm font-semibold), :199-209 (row py-1.5 text-sm muted label, keys as kbd keycaps). The
// two-column grid and the footer link are not built. Bento and Nordic keep the older look.
// Mirrors: Rust shortcuts.rs show_help :395 (tabs Mail, Calendar, Contacts, Files; a header row per section; keys as chips; single-key rows greyed while single-key shortcuts are off);
// Bulwark components/keyboard-shortcuts-modal.tsx. The model is ShortcutSheetController (Presentation). Owner menus.

using Barbican.App.Motion;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Palette;
using Windows.Foundation;

namespace Barbican.App.Views.Shortcuts;

/// <summary>Draws the ? sheet.</summary>
public sealed partial class ShortcutSheetView : UserControl
{
    private readonly ShortcutSheetController _sheet = ViewHost.Get<ShortcutSheetController>();
    private bool _open;

    public ShortcutSheetView()
    {
        InitializeComponent();
        _sheet.PropertyChanged += (_, _) => Render();
        Backdrop.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, Backdrop))
            {
                _sheet.Close();
            }
        };
        SizeChanged += (_, _) =>
        {
            Backdrop.Width = Math.Max(ActualWidth, 1);
            Backdrop.Height = Math.Max(ActualHeight, 1);
        };
    }

    private void Render()
    {
        if (!_sheet.IsOpen)
        {
            _open = false;
            SheetPopup.IsOpen = false;
            return;
        }

        BuildTabs();
        BuildRows();
        Note.Visibility = _sheet.SingleKeysOff ? Visibility.Visible : Visibility.Collapsed;
        if (!_open)
        {
            _open = true;
            Backdrop.Width = Math.Max(ActualWidth, 1);
            Backdrop.Height = Math.Max(ActualHeight, 1);
            ApplyLook();
            SheetPopup.IsOpen = true;
            MotionRuntime.FadeIn(Backdrop);
            PopIn.Run(Card, new Point(0.5, 0.5));
        }
    }

    private bool _bulwark;

    // read at each open; the tabs and rows are built just before it (Render calls BuildTabs and BuildRows first, so open again after a style flip for the whole look)
    private void ApplyLook()
    {
        _bulwark = StyleState.Current.IsBulwark;
        Backdrop.Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(_bulwark ? (byte)0x80 : (byte)0x66, 0, 0, 0));
        Card.CornerRadius = StyleState.Current.DialogRadius;
        Card.Padding = new Thickness(0, _bulwark ? 24 : 16, 0, 8);
        TitleText.FontSize = _bulwark ? 18 : 16;
        TabRule.Visibility = _bulwark ? Visibility.Visible : Visibility.Collapsed;
        var gutter = _bulwark ? 24 : 20;
        TitleText.Margin = new Thickness(gutter, 0, gutter, _bulwark ? 16 : 10);
        Tabs.Margin = new Thickness(gutter, 0, gutter, _bulwark ? 0 : 8);
        TabRule.Margin = new Thickness(gutter, 0, gutter, 0);
        Rows.Margin = new Thickness(gutter, 0, gutter, 0);
        BuildTabs();
        BuildRows();
    }

    private void BuildTabs()
    {
        Tabs.Children.Clear();
        for (var i = 0; i < ShortcutSheetController.Tabs.Count; i++)
        {
            var tab = i;
            var active = tab == _sheet.Tab;
            var chip = _bulwark
                ? new Border
                {
                    Padding = new Thickness(12, 8, 12, 8),
                    BorderThickness = new Thickness(0, 0, 0, 2),
                    BorderBrush = active ? AppResources.Brush("Accent") : new SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                    Child = new TextBlock { Text = ShortcutSheetController.Tabs[i], FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = AppResources.Brush(active ? "Text" : "Dim") },
                }
                : new Border
                {
                    Padding = new Thickness(12, 4, 12, 4),
                    CornerRadius = new CornerRadius(8),
                    Background = active ? AppResources.Brush("Sel") : AppResources.Brush("Chip"),
                    Child = new TextBlock { Text = ShortcutSheetController.Tabs[i], FontSize = 12, Foreground = active ? AppResources.Brush("SelText") : AppResources.Brush("Dim") },
                };
            chip.Tapped += (_, e) =>
            {
                e.Handled = true;
                _sheet.SelectTab(tab);
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(chip, "shortcuts.tab." + ShortcutSheetController.Tabs[i]);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chip, ShortcutSheetController.Tabs[i]);
            Tabs.Children.Add(chip);
        }
    }

    private void BuildRows()
    {
        Rows.Children.Clear();
        foreach (var row in _sheet.Rows)
        {
            if (row.Section.Length > 0)
            {
                Rows.Children.Add(_bulwark
                    ? new TextBlock { Text = row.Section, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AppResources.Brush("Text"), Margin = new Thickness(0, 16, 0, 8) }
                    : new TextBlock { Text = row.Section, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AppResources.Brush("Faint"), Margin = new Thickness(0, 12, 0, 4) });
                continue;
            }

            var off = _sheet.IsOff(row);
            var grid = new Grid { Height = _bulwark ? 32 : 30, ColumnSpacing = 12, Opacity = off ? 0.45 : 1 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = row.Label, FontSize = _bulwark ? 14 : 13, Foreground = AppResources.Brush(_bulwark ? "Dim" : "Body"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            foreach (var key in row.Keys)
            {
                keys.Children.Add(new Border
                {
                    Padding = new Thickness(7, 2, 7, 2),
                    CornerRadius = new CornerRadius(_bulwark ? 4 : 6),
                    BorderThickness = new Thickness(_bulwark ? 1 : 0),
                    BorderBrush = AppResources.Brush("Line"),
                    Background = AppResources.Brush("Chip"),
                    Child = new TextBlock { Text = key, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AppResources.Brush("Text") },
                });
            }

            Grid.SetColumn(keys, 1);
            grid.Children.Add(keys);
            Rows.Children.Add(grid);
        }
    }
}
