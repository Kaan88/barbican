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
            SheetPopup.IsOpen = true;
            MotionRuntime.FadeIn(Backdrop);
            PopIn.Run(Card, new Point(0.5, 0.5));
        }
    }

    private void BuildTabs()
    {
        Tabs.Children.Clear();
        for (var i = 0; i < ShortcutSheetController.Tabs.Count; i++)
        {
            var tab = i;
            var active = tab == _sheet.Tab;
            var chip = new Border
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
                Rows.Children.Add(new TextBlock { Text = row.Section, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AppResources.Brush("Faint"), Margin = new Thickness(0, 12, 0, 4) });
                continue;
            }

            var off = _sheet.IsOff(row);
            var grid = new Grid { Height = 30, ColumnSpacing = 12, Opacity = off ? 0.45 : 1 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = row.Label, FontSize = 13, Foreground = AppResources.Brush("Body"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            foreach (var key in row.Keys)
            {
                keys.Children.Add(new Border
                {
                    Padding = new Thickness(7, 2, 7, 2),
                    CornerRadius = new CornerRadius(6),
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
