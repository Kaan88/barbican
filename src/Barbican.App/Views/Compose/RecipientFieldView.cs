// Mirrors (chip radius/size): Bulwark email-composer.tsx:7505 (px-2 py-0.5 rounded-md text-sm border), suggestions :6933 (rounded-md). Rust chips.rs + recipients.slint (chips with initials, invalid marking, the autocomplete list under the field); Bulwark components/email/email-composer.tsx RecipientInput (:7314-7762: create, Backspace arms, paste splits,
// double click, chip menu MENU-ADDR, keyboard); I-246..I-249. One recipient field: label, chips, the input, and the suggestion list (a popup in the window, never an OS window). The data is RecipientFieldViewModel.

#pragma warning disable CA1859, CA1822 // the card builders return UIElement by design (one slot, many kinds)

using Barbican.App.Controls;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Menus;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Barbican.App.Views.Compose;

/// <summary>A To, Cc or Bcc row.</summary>
public sealed class RecipientFieldView : UserControl
{
    private static readonly TimeSpan BlurDelay = TimeSpan.FromMilliseconds(180);

    private readonly Grid _root = new() { ColumnSpacing = 10 };
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Top, Width = 54, FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ChipPanel _panel = new();
    private bool _bulwark;
    private readonly TextBox _input = new() { Style = (Style)Application.Current.Resources["TextBox.Bare"], FontSize = 13, MinHeight = 26, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ContentPresenter _trailing = new() { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly Border _listBox;
    private RecipientFieldViewModel? _field;
    private bool _syncing;
    private int _picked;

    public RecipientFieldView()
    {
        _label.Foreground = AppResources.Brush("Dim");
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _root.MinHeight = 34;
        Grid.SetColumn(_panel, 1);
        Grid.SetColumn(_trailing, 2);
        _root.Children.Add(_label);
        _root.Children.Add(_panel);
        _root.Children.Add(_trailing);
        _listBox = new Border
        {
            Background = AppResources.Brush("Popup"),
            BorderBrush = AppResources.Brush("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            MinWidth = 280,
            MaxWidth = 420,
            Padding = new Thickness(4),
            Child = _list,
        };
        AutomationProperties.SetAutomationId(_listBox, "composer.suggestions");
        // the list sits in the row under the field (an in-layout list: no popup window, ADR 0007)
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_listBox, 1);
        Grid.SetColumn(_listBox, 1);
        _listBox.HorizontalAlignment = HorizontalAlignment.Left;
        _listBox.Visibility = Visibility.Collapsed;
        _root.Children.Add(_listBox);
        Content = _root;
        _input.TextChanged += OnTextChanged;
        _input.KeyDown += OnKeyDown;
        _input.LostFocus += OnLostFocus;
        RightTapped += OnRightTapped;
    }

    /// <summary>"To", "Cc" or "Bcc".</summary>
    public string Label
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    /// <summary>A control at the right end of the row (the Cc and Bcc toggles on the To row).</summary>
    public object? Trailing
    {
        get => _trailing.Content;
        set => _trailing.Content = value;
    }

    /// <summary>Applies the composer theme without changing recipient behavior.</summary>
    public void ApplyComposerStyle(bool bulwark)
    {
        var changed = _bulwark != bulwark;
        _bulwark = bulwark;
        _listBox.CornerRadius = bulwark ? ViewHost.Get<StyleState>().ButtonRadius : new CornerRadius(10);
        _root.Padding = bulwark ? new Thickness(16, 10, 16, 10) : new Thickness(0);
        _root.ColumnSpacing = bulwark ? 8 : 10;
        _label.Width = bulwark ? 64 : 54;
        _label.FontSize = bulwark ? 14 : 12.5;
        _input.FontSize = bulwark ? 14 : 13;
        var label = _label.Text.TrimEnd(':');
        _label.Text = bulwark ? label + ":" : label;
        if (changed && _field is not null)
        {
            Rebuild();
        }
    }

    /// <summary>The input takes keyboard focus.</summary>
    public void FocusInput() => _ = _input.Focus(FocusState.Programmatic);

    /// <summary>Binds the row to a field (null unbinds).</summary>
    public void Bind(RecipientFieldViewModel? field)
    {
        if (_field is not null)
        {
            _field.PropertyChanged -= OnFieldChanged;
        }

        _field = field;
        if (field is null)
        {
            return;
        }

        var id = field.Name.ToLowerInvariant();
        AutomationProperties.SetAutomationId(_root, "composer." + id);
        AutomationProperties.SetName(_root, field.Name);
        AutomationProperties.SetAutomationId(_input, "composer." + id + ".input");
        AutomationProperties.SetName(_input, field.Name + " recipients");
        field.PropertyChanged += OnFieldChanged;
        Rebuild();
        SyncList();
    }

    private void OnFieldChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RecipientFieldViewModel.Chips):
                Rebuild();
                break;
            case nameof(RecipientFieldViewModel.Input):
                SetText(_field!.Input);
                break;
            case nameof(RecipientFieldViewModel.Suggestions) or nameof(RecipientFieldViewModel.Highlight) or nameof(RecipientFieldViewModel.IsOpen):
                SyncList();
                break;
            default:
                break;
        }
    }

    private void SetText(string text)
    {
        if (!string.Equals(_input.Text, text, StringComparison.Ordinal))
        {
            _syncing = true;
            _input.Text = text;
            _input.SelectionStart = text.Length;
            _syncing = false;
        }
    }

    private void Rebuild()
    {
        if (_field is not { } f)
        {
            return;
        }

        _panel.Children.Clear();
        for (var i = 0; i < f.Chips.Count; i++)
        {
            _panel.Children.Add(Chip(f.Chips[i], i));
        }

        _panel.Children.Add(_input);
    }

    private UIElement Chip(RecipientChip chip, int index)
    {
        var (bg, fg) = chip.Kind switch
        {
            ChipKind.Invalid => (AppResources.Brush("RoseBg"), AppResources.Brush("RoseFg")),
            ChipKind.Group => (AppResources.Brush("SkyBg"), AppResources.Brush("SkyFg")),
            _ => (AppResources.Brush("Chip"), AppResources.Brush("Body")),
        };
        var avatar = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = chip.Kind == ChipKind.Person ? AppResources.Brush("Tile2") : Transparent(),
            Child = new TextBlock { Text = chip.Initials, FontSize = 9, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var remove = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Content = new SIcon { Kind = "Close", Size = 10 } };
        AutomationProperties.SetAutomationId(remove, $"composer.{_field!.Name.ToLowerInvariant()}.chip.{index}.remove");
        AutomationProperties.SetName(remove, "Remove " + chip.Label);
        remove.Click += (_, _) => _field!.Remove(index);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        row.Children.Add(avatar);
        row.Children.Add(new TextBlock { Text = chip.Label, FontSize = _bulwark ? 14 : 12.5, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(remove);
        var box = new Border
        {
            Background = bg,
            BorderBrush = chip.Armed ? AppResources.Brush("Accent") : AppResources.Brush("Line"),
            BorderThickness = new Thickness(chip.Armed ? 2 : 1),
            // email-composer.tsx:7505: inline-flex px-2 py-0.5 rounded-md text-sm border border-border
            CornerRadius = _bulwark ? ViewHost.Get<StyleState>().ButtonRadius : new CornerRadius(13),
            Height = 26,
            Padding = new Thickness(3, 0, 3, 0),
            Child = row,
        };
        AutomationProperties.SetAutomationId(box, $"composer.{_field.Name.ToLowerInvariant()}.chip.{index}");
        AutomationProperties.SetName(box, chip.Kind == ChipKind.Invalid ? "Invalid address " + chip.Full : chip.Full);
        box.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            _field!.EditChip(index);
            FocusInput();
        };
        box.RightTapped += (_, e) =>
        {
            e.Handled = true;
            OpenChipMenu(chip, e.GetPosition(null));
        };
        return box;
    }

    private static SolidColorBrush Transparent() => new(Microsoft.UI.Colors.Transparent);

    // MENU-ADDR on a chip: copy the address, new message, add to contacts, find from this sender (the menus stream's rows)
    private void OpenChipMenu(RecipientChip chip, Windows.Foundation.Point at)
    {
        if (chip.Kind != ChipKind.Person || ViewHost.Find<IContextMenuService>() is not { } menus)
        {
            return;
        }

        var (_, email) = Barbican.Core.Compose.Recipients.RecipientText.SplitMailbox(chip.Full);
        menus.Show(new MenuContext(MenuIds.Address, [], null, null, email, null, Barbican.Presentation.Commands.CommandSurface.ContextMenu), new MenuAnchor(at.X, at.Y, null));
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _field is null)
        {
            return;
        }

        var text = _input.Text;
        // a comma or semicolon typed after a whole address commits it (the key event may not carry the character on every layout)
        if (text.Length > 1 && text[^1] is ',' or ';' && _field.Key(text[^1].ToString(), false))
        {
            SetText(_field.Input);
            return;
        }

        _field.Edited(text);
        SetText(_field.Input);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_field is null)
        {
            return;
        }

        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl)
        {
            return;
        }

        var name = e.Key switch
        {
            VirtualKey.Enter => "Enter",
            VirtualKey.Tab => "Tab",
            VirtualKey.Escape => "Escape",
            VirtualKey.Down => "ArrowDown",
            VirtualKey.Up => "ArrowUp",
            VirtualKey.Back => "Backspace",
            VirtualKey.Space => " ",
            _ => null,
        };
        if (name is not null && _field.Key(name, shift))
        {
            e.Handled = true;
            SetText(_field.Input);
        }
    }

    // a click on a suggestion wins over the blur commit: the blur waits a moment and checks that nothing was picked meanwhile
    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_field is null)
        {
            return;
        }

        var mark = _picked;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = BlurDelay;
        timer.IsRepeating = false;
        timer.Tick += (t, _) =>
        {
            t.Stop();
            if (_field is { } f && mark == _picked)
            {
                f.Blur();
                SetText(f.Input);
            }
        };
        timer.Start();
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // a chip handles its own menu; the field itself has none
    }

    private void SyncList()
    {
        _list.Children.Clear();
        if (_field is not { IsOpen: true } f)
        {
            _listBox.Visibility = Visibility.Collapsed;
            return;
        }

        for (var i = 0; i < f.Suggestions.Count; i++)
        {
            _list.Children.Add(Row(f.Suggestions[i], i, i == f.Highlight));
        }

        _listBox.Visibility = Visibility.Visible;
    }

    private UIElement Row(SuggestionRow row, int index, bool highlighted)
    {
        var avatar = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = AppResources.Brush(row.IsGroup ? "SkyBg" : "Tile2"),
            Child = row.IsGroup
                ? new SIcon { Kind = "Users", Size = 14, Foreground = AppResources.Brush("SkyFg"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                : new TextBlock { Text = row.Initials, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = AppResources.Brush("Body"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = row.Name, FontSize = 13, Foreground = AppResources.Brush("Text"), TextTrimming = TextTrimming.CharacterEllipsis });
        if (row.Detail.Length > 0)
        {
            text.Children.Add(new TextBlock { Text = row.Detail, FontSize = 11.5, Foreground = AppResources.Brush("Dim"), TextTrimming = TextTrimming.CharacterEllipsis });
        }

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(avatar);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var box = new Border
        {
            Padding = new Thickness(8, 5, 8, 5),
            CornerRadius = new CornerRadius(8),
            Background = highlighted ? AppResources.Brush("Sel") : Transparent(),
            Child = grid,
        };
        AutomationProperties.SetAutomationId(box, $"composer.suggestion.{index}");
        AutomationProperties.SetName(box, row.Name + (row.Detail.Length > 0 ? ", " + row.Detail : string.Empty));
        box.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _picked++;
            _field?.Pick(index);
            SetText(_field?.Input ?? string.Empty);
            FocusInput();
        };
        return box;
    }
}
