// Bulwark 1.21.2 reference (default theme): components/command-palette/command-palette.tsx:336 (scrim bg-black/50, card near the top at 12 vh), :344 (w-full max-w-xl rounded-lg border bg-background
// shadow-xl), :350-369 (search icon, borderless 48 px input, Esc keycap, border-bottom), :391 (group label 11 px semibold muted), :404-418 (row mx-1 gap-2.5 rounded-md px-2.5 py-2 text-sm, active
// bg-muted, 16 px muted icon, keycaps with border and muted fill). Bento and Nordic keep the older look.
// Mirrors: Rust palette.rs open :246 / rebuild :269 / move_active :423 / enter :437; Bulwark components/command-palette (Ctrl+K opens, typing filters, arrows move, Enter runs, Esc closes,
// Tab switches the scope); feel scenario palette_open (Ctrl+K, type, Esc). The model is PaletteController (Presentation); this draws it. Owner menus.

using Barbican.App.Controls;
using Barbican.App.Motion;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Input;
using Barbican.Presentation.Palette;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Barbican.App.Views.Palette;

/// <summary>Draws the command palette.</summary>
public sealed partial class PaletteView : UserControl
{
    private readonly PaletteController _palette = ViewHost.Get<PaletteController>();
    private readonly List<Border> _commandRows = [];
    private readonly List<int> _commandLines = [];
    private IReadOnlyList<PaletteRow>? _rowsShown;
    private bool _open;
    private Control? _restoreFocus;
    private bool _syncing;
    private bool _bulwark;

    public PaletteView()
    {
        InitializeComponent();
        _palette.PropertyChanged += (_, e) => OnModelChanged(e.PropertyName);
        Backdrop.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, Backdrop))
            {
                _palette.Close();
            }
        };
        SizeChanged += (_, _) => FitBackdrop();
    }

    private void OnModelChanged(string? property)
    {
        if (string.Equals(property, nameof(PaletteController.IsOpen), StringComparison.Ordinal))
        {
            if (_palette.IsOpen && !_open)
            {
                Open();
            }
            else if (!_palette.IsOpen && _open)
            {
                Close();
            }
        }

        if (_open && (property is null || string.Equals(property, nameof(PaletteController.Rows), StringComparison.Ordinal)))
        {
            RebuildRows();
        }

        if (_open && (property is null || string.Equals(property, nameof(PaletteController.Highlight), StringComparison.Ordinal)))
        {
            Highlight();
        }

        if (_open && string.Equals(property, nameof(PaletteController.Scope), StringComparison.Ordinal))
        {
            BuildTabs();
        }
    }

    private void Open()
    {
        _open = true;
        _restoreFocus = (XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot)) as Control;
        FitBackdrop();
        _syncing = true;
        Input.Text = string.Empty;
        _syncing = false;
        BuildTabs();
        ApplyLook();
        PalettePopup.IsOpen = true;
        MotionRuntime.FadeIn(Backdrop);
        PopIn.Run(Card, new Point(0.5, 0));
        _ = DispatcherQueue.TryEnqueue(() => Input.Focus(FocusState.Programmatic));
    }

    // read at each open, so a style flip applies to the next palette
    private void ApplyLook()
    {
        _bulwark = StyleState.Current.IsBulwark;
        Backdrop.Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(_bulwark ? (byte)0x80 : (byte)0x44, 0, 0, 0));
        Card.Width = _bulwark ? 576 : 560;
        Card.Margin = new Thickness(0, _bulwark ? Math.Max(16, ActualHeight * 0.12) : 72, 0, 0);
        Card.CornerRadius = StyleState.Current.DialogRadius;
        CardShadow.Apply(Card, CardDepth.ExtraLarge, Backdrop);
        SearchGlyph.Visibility = EscKey.Visibility = InputRule.Visibility = _bulwark ? Visibility.Visible : Visibility.Collapsed;
        if (_bulwark)
        {
            Input.Style = (Style)Application.Current.Resources["TextBox.Bare"];
            Input.Margin = new Thickness(0);
            Input.Height = 48;
            Input.FontSize = 14;
            Input.VerticalContentAlignment = VerticalAlignment.Center;
        }
        else
        {
            Input.ClearValue(StyleProperty);
            Input.ClearValue(HeightProperty);
            Input.Margin = new Thickness(12);
            Input.FontSize = 14;
        }

        Rows.Margin = new Thickness(_bulwark ? 4 : 6, _bulwark ? 4 : 0, _bulwark ? 4 : 6, 8);
    }

    private void Close()
    {
        _open = false;
        PalettePopup.IsOpen = false;
        _ = _restoreFocus?.Focus(FocusState.Programmatic);
        _restoreFocus = null;
        _rowsShown = null;
    }

    private void FitBackdrop()
    {
        Backdrop.Width = Math.Max(ActualWidth, 1);
        Backdrop.Height = Math.Max(ActualHeight, 1);
    }

    private void BuildTabs()
    {
        Tabs.Children.Clear();
        var scopes = _palette.Scopes;
        Tabs.Visibility = scopes.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var scope in scopes)
        {
            var active = string.Equals(scope, _palette.Scope, StringComparison.Ordinal);
            Tabs.Children.Add(new Border
            {
                Padding = new Thickness(10, 3, 10, 3),
                CornerRadius = new CornerRadius(8),
                Background = active ? AppResources.Brush("Sel") : AppResources.Brush("Chip"),
                Child = new TextBlock { Text = scope, FontSize = 12, Foreground = active ? AppResources.Brush("SelText") : AppResources.Brush("Dim") },
            });
        }
    }

    // The rows follow the model in place of rebuilding on every key: the old rows stay until the new list is ready (feel: nothing blanks)
    private void RebuildRows()
    {
        var rows = _palette.Rows;
        if (ReferenceEquals(rows, _rowsShown))
        {
            return;
        }

        _rowsShown = rows;
        var children = new List<UIElement>(rows.Count);
        _commandRows.Clear();
        _commandLines.Clear();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.IsHeader)
            {
                children.Add(new TextBlock { Text = row.Header, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AppResources.Brush("Faint"), Margin = _bulwark ? new Thickness(12, 8, 12, 4) : new Thickness(10, 10, 10, 4) });
                continue;
            }

            var line = i;
            var element = BuildRow(row, line);
            _commandRows.Add(element);
            _commandLines.Add(line);
            children.Add(element);
        }

        Rows.Children.Clear();
        foreach (var child in children)
        {
            Rows.Children.Add(child);
        }

        Highlight();
    }

    private Border BuildRow(PaletteRow row, int line)
    {
        var command = row.Command!;
        var foreground = row.Enabled ? AppResources.Brush("Text") : AppResources.Brush("Faint");
        var grid = new Grid { Margin = new Thickness(10, 0, 10, 0), ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new SIcon { Kind = IconKey(command.Icon), Size = 16, Foreground = _bulwark && row.Enabled ? AppResources.Brush("Dim") : foreground, VerticalAlignment = VerticalAlignment.Center });
        var label = new TextBlock { Text = command.Label, FontSize = _bulwark ? 14 : 13, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        if (command.Shortcut.Length > 0)
        {
            FrameworkElement hint = _bulwark
                ? new Border
                {
                    BorderBrush = AppResources.Brush("Line"),
                    BorderThickness = new Thickness(1),
                    Background = AppResources.Brush("FeedbackHover"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = command.Shortcut, FontSize = 10, FontFamily = new FontFamily("Consolas"), Foreground = AppResources.Brush("Dim") },
                }
                : new TextBlock { Text = command.Shortcut, FontSize = 11, Foreground = AppResources.Brush("Faint"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(hint, 2);
            grid.Children.Add(hint);
        }

        var border = new Border { Height = _bulwark ? 36 : 34, CornerRadius = _bulwark ? new CornerRadius(StyleState.Current.Metrics.ButtonRadius) : new CornerRadius(8), Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0)), Child = grid };
        border.Tapped += (_, e) =>
        {
            e.Handled = true;
            _ = _palette.PickAsync(_commandLines.IndexOf(line));
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(border, "palette.row." + command.Id);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(border, command.Label);
        return border;
    }

    private void Highlight()
    {
        for (var i = 0; i < _commandRows.Count; i++)
        {
            _commandRows[i].Background = _commandLines[i] == _palette.Highlight ? AppResources.Brush(_bulwark ? "FeedbackHover" : "Hover") : new SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }
    }

    // palette icon names are lower-case words ("reply-all"); the line-icon set names them in Pascal case
    private static string IconKey(string name) => string.Concat(name.Split('-').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncing && _open)
        {
            _ = _palette.SetTextAsync(Input.Text);
        }
    }

    // a TextBox keeps its keys away from the shell's router, so the palette's keys are handled here
    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var name = e.Key switch
        {
            global::Windows.System.VirtualKey.Escape => "Escape",
            global::Windows.System.VirtualKey.Down => "ArrowDown",
            global::Windows.System.VirtualKey.Up => "ArrowUp",
            global::Windows.System.VirtualKey.Enter => "Enter",
            global::Windows.System.VirtualKey.Tab => "Tab",
            _ => null,
        };
        if (name is not null && _palette.HandleKey(new KeyChord(name)))
        {
            e.Handled = true;
        }
    }
}
