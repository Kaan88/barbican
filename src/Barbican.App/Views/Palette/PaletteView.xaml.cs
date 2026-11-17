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
        PalettePopup.IsOpen = true;
        MotionRuntime.FadeIn(Backdrop);
        PopIn.Run(Card, new Point(0.5, 0));
        _ = DispatcherQueue.TryEnqueue(() => Input.Focus(FocusState.Programmatic));
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
                children.Add(new TextBlock { Text = row.Header, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AppResources.Brush("Faint"), Margin = new Thickness(10, 10, 10, 4) });
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
        grid.Children.Add(new SIcon { Kind = IconKey(command.Icon), Size = 16, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center });
        var label = new TextBlock { Text = command.Label, FontSize = 13, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        if (command.Shortcut.Length > 0)
        {
            var hint = new TextBlock { Text = command.Shortcut, FontSize = 11, Foreground = AppResources.Brush("Faint"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(hint, 2);
            grid.Children.Add(hint);
        }

        var border = new Border { Height = 34, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0)), Child = grid };
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
            _commandRows[i].Background = _commandLines[i] == _palette.Highlight ? AppResources.Brush("Hover") : new SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0));
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
