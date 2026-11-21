// Mirrors: Bulwark components/search/ (search-box.tsx, search-suggestions.tsx, search-chips.tsx, search-filter-panel.tsx, filter-panel-host.tsx), components/ui/toolbar.tsx:36-120 (ToolbarSearch, ToolbarSearchAction), Rust search_ui.rs.
// I-023, I-146 (Enter searches, Escape clears), I-272 (type, Enter, empty Enter), I-273 (clear X), I-274 (suggestions), I-275 (chips), I-276 (filter toggle with count), I-277..I-280 (filter panel: live search, presets,
// read filter, starred, attachment, "Search in", Clear all).
// Bulwark metrics matched in the default theme: field h-8 rounded-md border-input (hover muted-foreground, focus ring), key cap hidden while focused, filter toggle active tint and 16 px count badge (mail-app.tsx:6649-6672),
// chips (search-chips.tsx:78-110), dropdown rows px-3 py-2 text-sm with hover / active fill and hover-only remove and "To" buttons (search-suggestions.tsx:70-195), in-flow filter panel (search-filter-panel.tsx:97-274).
// The model (SearchBoxModel) holds the behaviour; this view only draws it: chips, suggestions and the panel states are redrawn from the model when it changes, and the field's own text is never rewritten while the
// user types (no caret jump, no flicker). Feedback is a one-frame colour swap or the shared button / toggle styles: no bounce, no scale, no fade.

using Barbican.App.Controls;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Mail.Rules;
using Barbican.Core.Search;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Search;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Views.Search;

/// <summary>The search box of the message list.</summary>
public sealed partial class SearchBoxView : UserControl
{
    private static readonly Brush ClearBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    private static readonly TimeSpan FieldDebounce = TimeSpan.FromMilliseconds(350);

    private readonly MessageListViewModel _list;
    private readonly SearchBoxModel _model;
    private readonly List<(string Token, Button Button, string Label, string Icon)> _toggles = [];
    private readonly List<(int Days, Button Button)> _dates = [];
    private readonly List<(string Value, Button Button)> _places = [];
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _fieldTimer;
    private bool _syncing;
    private bool _redrawQueued;
    private bool _focused;
    private bool _hover;

    public SearchBoxView()
    {
        _list = ViewHost.Get<MessageListViewModel>();
        _model = ViewHost.Get<SearchBoxModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        _fieldTimer = DispatcherQueue.CreateTimer();
        _fieldTimer.Interval = FieldDebounce;
        _fieldTimer.IsRepeating = false;
        _fieldTimer.Tick += (_, _) => _ = RunFieldsAsync();
        _list.FocusSearchRequested += (_, _) => SearchBox.Focus(FocusState.Programmatic);
        _model.PropertyChanged += (_, _) => QueueRedraw();
        Look.PropertyChanged += (_, _) => ApplyFrame();
        _list.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(MessageListViewModel.SearchText), StringComparison.Ordinal) && !string.Equals(_list.SearchText, SearchBox.Text, StringComparison.Ordinal))
            {
                // a folder opened (or a search ran elsewhere): the box follows the list
                _model.Sync(_list.SearchText);
            }
        };
        Redraw();
    }

    public StyleState Look { get; }

    /// <summary>Text size of the field: text-sm (14) in Bulwark, 13 in Bento and Nordic.</summary>
    public static double FieldFont(bool bulwark) => bulwark ? 14 : 13;

    private static Brush Brush(string key) => Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b ? b : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    private static Style? StyleOf(string key) => Application.Current.Resources.TryGetValue(key, out var v) ? v as Style : null;

    // One model change raises several property notifications (Changed()); the controls are redrawn once, before the next frame, not once per notification (search_type: one quantum per key).
    private void QueueRedraw()
    {
        if (_redrawQueued)
        {
            return;
        }

        _redrawQueued = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
        {
            _redrawQueued = false;
            Redraw();
        }))
        {
            _redrawQueued = false;
            Redraw();
        }
    }

    // input.tsx:13-18: bg-background, border-input, hover:border-muted-foreground, focus-visible:border-ring (colour only). Bento / Nordic keep the transparent hairline field.
    private void ApplyFrame()
    {
        if (Look.IsBulwark)
        {
            Frame.Background = Brush("Bg");
            Frame.BorderBrush = Brush(_focused ? "FeedbackFocus" : _hover ? "Dim" : "Input");
        }
        else
        {
            Frame.Background = ClearBrush;
            Frame.BorderBrush = Brush("Line");
        }

        // toolbar.tsx:98: the key cap shows while the field is empty and hidden while it has focus (group-focus-within:invisible)
        KeyCap.Visibility = _model.Text.Length == 0 && !_focused ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnFrameEnter(object sender, PointerRoutedEventArgs e)
    {
        _hover = true;
        ApplyFrame();
    }

    private void OnFrameExit(object sender, PointerRoutedEventArgs e)
    {
        _hover = false;
        ApplyFrame();
    }

    private void Redraw()
    {
        if (!string.Equals(SearchBox.Text, _model.Text, StringComparison.Ordinal))
        {
            _syncing = true;
            SearchBox.Text = _model.Text;
            SearchBox.SelectionStart = SearchBox.Text.Length;
            _syncing = false;
        }

        ClearButton.Visibility = _model.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var count = _model.FilterCount;
        FilterCount.Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        FilterButton.IsChecked = _model.FilterOpen || count > 0;
        FilterBadge.Visibility = !_model.FilterOpen && count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFrame();
        DrawChips();
        DrawSuggestions();
        DrawFilterPanel();
    }

    private void DrawChips()
    {
        var chips = _model.Chips;
        ChipsPanel.Visibility = chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ChipsPanel.Children.Clear();
        foreach (var chip in chips)
        {
            ChipsPanel.Children.Add(ChipOf(chip));
        }

        if (chips.Count > 1)
        {
            var all = new Button
            {
                Content = new TextBlock { Text = "Clear all", FontSize = 12 },
                Style = StyleOf("Button.Icon"),
                Height = 22,
                Padding = new Thickness(6, 0, 6, 0),
                CornerRadius = Look.ButtonRadius,
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetAutomationId(all, "list.search.clear-all");
            AutomationProperties.SetName(all, "Clear all");
            all.Click += (_, _) => _ = _model.RemoveChipAsync(string.Empty);
            ChipsPanel.Children.Add(all);
        }
    }

    // search-chips.tsx:80-102: inline-flex gap-1 px-2 py-0.5 rounded-full text-xs bg-primary/10 text-primary border border-primary/20; the X is a p-1.5 rounded-full button pulled out by -my-1 -me-1.
    private Border ChipOf(SearchChip chip)
    {
        var fg = Brush("FeedbackActiveFg");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock { Text = chip.Label, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = fg, VerticalAlignment = VerticalAlignment.Center });
        if (chip.Value.Length > 0)
        {
            row.Children.Add(new TextBlock { Text = ":", FontSize = 12, Foreground = fg, Opacity = 0.6, Margin = new Thickness(-2, 0, -2, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = chip.Value, FontSize = 12, Foreground = fg, MaxWidth = 96, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        }

        var x = new Button
        {
            Content = new SIcon { Kind = "Close", Size = 12, StrokeWidth = 2 },
            Style = StyleOf("Button.Icon"),
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, -4, -6, -4),
            Foreground = fg,
        };
        x.Click += (_, _) => _ = _model.RemoveChipAsync(chip.Key);
        AutomationProperties.SetAutomationId(x, $"list.search.chip.{chip.Key}");
        AutomationProperties.SetName(x, $"Remove filter {chip.Label}");
        row.Children.Add(x);
        return new Border
        {
            Child = row,
            Height = 22,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(11),
            Background = Brush("FeedbackActive"),
            BorderBrush = Brush("Line"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // search-suggestions.tsx:76 (absolute top-full start-0 end-0 mt-1 bg-background border rounded-md shadow-lg max-h-72 py-1): an in-window Popup under the field, so the list below does not move.
    private void DrawSuggestions()
    {
        SuggestList.Children.Clear();
        var open = _model.SuggestionsOpen;
        SuggestPopup.IsOpen = open;
        if (!open)
        {
            return;
        }

        SuggestPanel.Width = Math.Max(0, FieldHost.ActualWidth);
        SuggestPopup.VerticalOffset = FieldHost.ActualHeight + 4;
        var query = _model.Text.Trim();
        var suggestions = _model.Suggestions;
        var firstPerson = -1;
        var hasRecent = false;
        for (var i = 0; i < suggestions.Count; i++)
        {
            if (suggestions[i] is SearchSuggestion.Person && firstPerson < 0)
            {
                firstPerson = i;
            }
            else if (suggestions[i] is SearchSuggestion.Recent)
            {
                hasRecent = true;
            }
        }

        for (var i = 0; i < suggestions.Count; i++)
        {
            if (i == 0 && suggestions[i] is SearchSuggestion.Recent)
            {
                SuggestList.Children.Add(Heading("Recent searches", false));
            }
            else if (i == firstPerson)
            {
                SuggestList.Children.Add(Heading("People", hasRecent));
            }

            SuggestList.Children.Add(suggestions[i] switch
            {
                SearchSuggestion.Recent r => RecentRow(i, r, query),
                SearchSuggestion.Person p => PersonRow(i, p, query),
                _ => new Grid(),
            });
        }
    }

    // px-3 pt-1 pb-0.5 text-[0.6875rem] font-medium text-muted-foreground; the people group after recents gets mt-1 border-t pt-2.
    private static Border Heading(string text, bool separated) => new()
    {
        BorderBrush = Brush("Line"),
        BorderThickness = new Thickness(0, separated ? 1 : 0, 0, 0),
        Margin = new Thickness(0, separated ? 4 : 0, 0, 0),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = Brush("Dim"),
            Margin = new Thickness(12, separated ? 8 : 4, 12, 2),
        },
    };

    // The match is semibold inside the text (HighlightedText, search-suggestions.tsx:14-23).
    private static TextBlock Highlighted(string text, string query, Brush brush)
    {
        var tb = new TextBlock { FontSize = 14, Foreground = brush, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var at = query.Length == 0 ? -1 : text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            tb.Text = text;
            return tb;
        }

        if (at > 0)
        {
            tb.Inlines.Add(new Run { Text = text[..at] });
        }

        tb.Inlines.Add(new Run { Text = text.Substring(at, query.Length), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (at + query.Length < text.Length)
        {
            tb.Inlines.Add(new Run { Text = text[(at + query.Length)..] });
        }

        return tb;
    }

    // The row: px-3 py-2 gap-2, active bg-accent, hover bg-muted. The trailing buttons show on hover or while active (a one-frame swap, search-suggestions.tsx:122-135).
    private Grid Row(int index, string name, out Grid actions)
    {
        var row = new Grid { Background = ClearBrush, Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 8, MinHeight = 36 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AutomationProperties.SetAutomationId(row, $"list.search.suggestion.{index}");
        AutomationProperties.SetName(row, name);
        var active = index == _model.Active;
        var acts = new Grid { Opacity = active ? 1 : 0, VerticalAlignment = VerticalAlignment.Center };
        actions = acts;
        row.Background = active ? Brush("FeedbackHover") : ClearBrush;
        row.PointerEntered += (_, _) =>
        {
            row.Background = Brush("FeedbackHover");
            acts.Opacity = 1;
        };
        row.PointerExited += (_, _) =>
        {
            var on = index == _model.Active;
            row.Background = on ? Brush("FeedbackHover") : ClearBrush;
            acts.Opacity = on ? 1 : 0;
        };
        row.Tapped += (_, e) =>
        {
            if (!e.Handled)
            {
                _ = _model.PickAsync(index);
            }
        };
        return row;
    }

    private Grid RecentRow(int index, SearchSuggestion.Recent recent, string query)
    {
        var row = Row(index, recent.Query, out var actions);
        row.Children.Add(new SIcon { Kind = "Clock", Size = 16, StrokeWidth = 2, Foreground = Brush("Dim"), VerticalAlignment = VerticalAlignment.Center });
        var label = Highlighted(recent.Query, query, Brush("Text"));
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        var x = new Button
        {
            Content = new SIcon { Kind = "Close", Size = 14, StrokeWidth = 2 },
            Style = StyleOf("Button.Icon"),
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            IsTabStop = false,
        };
        AutomationProperties.SetName(x, "Remove from recent searches");
        AutomationProperties.SetAutomationId(x, $"list.search.suggestion.{index}.remove");
        x.Click += (_, _) => _model.RemoveRecent(index);
        actions.Children.Add(x);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        return row;
    }

    private Grid PersonRow(int index, SearchSuggestion.Person person, string query)
    {
        var row = Row(index, $"Search mail from {person.Email}", out var actions);
        var tint = RowText.TintIndex(person.Name.Length > 0 ? person.Name : person.Email);
        row.Children.Add(new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = Fmt.TintBg(tint),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = RecipientText.Initials(person.Name, person.Email),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = Fmt.TintFg(tint),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        // name first (max 60 %), the address gives way first (search-suggestions.tsx:158-166)
        var text = new Grid { ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        text.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        text.ColumnDefinitions.Add(new ColumnDefinition());
        if (person.Name.Length > 0)
        {
            var name = Highlighted(person.Name, query, Brush("Text"));
            name.MaxWidth = 140;
            text.Children.Add(name);
            var mail = Highlighted($"<{person.Email}>", query, Brush("Dim"));
            Grid.SetColumn(mail, 1);
            text.Children.Add(mail);
        }
        else
        {
            var mail = Highlighted(person.Email, query, Brush("Text"));
            Grid.SetColumnSpan(mail, 2);
            text.Children.Add(mail);
        }

        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        PersonTrailing(row, index, person, actions);
        return row;
    }

    // The "From" badge (bg-primary/10 text-primary) and the "To" button, search-suggestions.tsx:169-190.
    private void PersonTrailing(Grid row, int index, SearchSuggestion.Person person, Grid actions)
    {
        var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        trailing.Children.Add(new Border
        {
            Background = Brush("FeedbackActive"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock { Text = "From", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = Brush("FeedbackActiveFg") },
        });
        var to = new Button
        {
            Content = new TextBlock { Text = "To", FontSize = 11 },
            Style = StyleOf("Button.Icon"),
            Height = 22,
            Padding = new Thickness(6, 0, 6, 0),
            CornerRadius = new CornerRadius(4),
            BorderBrush = Brush("Line"),
            BorderThickness = new Thickness(1),
            IsTabStop = false,
        };
        AutomationProperties.SetName(to, $"Search mail to {person.Email}");
        AutomationProperties.SetAutomationId(to, $"list.search.suggestion.{index}.to");
        to.Click += (_, _) => _ = _model.PickPersonToAsync(index);
        actions.Children.Add(to);
        trailing.Children.Add(actions);
        Grid.SetColumn(trailing, 2);
        row.Children.Add(trailing);
    }

    // search-filter-panel.tsx:97-258: the toggle pills, From / To / Subject fields, the date and place choices, Clear all and Search. Built once (the fields keep their focus while the search runs live); Redraw only
    // refreshes the states. "Has words" and Size need operators the search syntax does not have (GlobalSearch.Operators): not drawn. The two selects are in-window choice rows (a ComboBox popup is out of window, ADR 0007).
    private void DrawFilterPanel()
    {
        FilterPanel.Visibility = _model.FilterOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!_model.FilterOpen)
        {
            return;
        }

        if (_toggles.Count == 0)
        {
            BuildFilterPanel();
        }

        _syncing = true;
        SetFieldText(FromBox, _model.GetOperator("from"));
        SetFieldText(ToBox, _model.GetOperator("to"));
        SetFieldText(SubjectBox, _model.GetOperator("subject"));
        _syncing = false;
        foreach (var (token, button, label, icon) in _toggles)
        {
            Pill(button, label, icon, _model.HasToken(token));
        }

        var after = _model.GetOperator("after");
        foreach (var (days, button) in _dates)
        {
            var on = days == 0 ? after.Length == 0 : string.Equals(after, DaysAgo(days), StringComparison.Ordinal);
            Pill(button, days switch { 7 => "Past week", 30 => "Past month", 365 => "Past year", _ => "Any time" }, null, on);
        }

        var place = _model.GetOperator("in");
        foreach (var (value, button) in _places)
        {
            Pill(button, value.Length == 0 ? "This folder" : char.ToUpperInvariant(value[0]) + value[1..], null, string.Equals(place, value, StringComparison.OrdinalIgnoreCase));
        }

        FilterClearAll.Visibility = _model.FilterCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetFieldText(TextBox box, string value)
    {
        if (!string.Equals(box.Text, value, StringComparison.Ordinal) && box.FocusState == FocusState.Unfocused)
        {
            box.Text = value;
        }
    }

    private static string DaysAgo(int days) => DateTime.Today.AddDays(-days).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private void BuildFilterPanel()
    {
        void Toggle(string token, string label, string icon, params string[] excludes)
        {
            var b = PillButton($"list.search.filter.{token}");
            b.Click += (_, _) => _ = _model.ToggleTokenAsync(token, excludes);
            FilterToggles.Children.Add(b);
            _toggles.Add((token, b, label, icon));
        }

        Toggle("is:unread", "Unread", "Mail", "is:read");
        Toggle("is:read", "Read", "Mail", "is:unread");
        Toggle("is:starred", "Starred", "Star", "is:unstarred");
        Toggle("has:attachment", "Attachment", "Clip");
        foreach (var days in new[] { 0, 7, 30, 365 })
        {
            var d = days;
            var b = PillButton($"list.search.filter.after.{days}");
            b.Click += (_, _) => _ = _model.SetOperatorAsync("after", d == 0 ? string.Empty : DaysAgo(d));
            DateChoices.Children.Add(b);
            _dates.Add((days, b));
        }

        foreach (var value in new[] { string.Empty, "trash", "spam" })
        {
            var v = value;
            var b = PillButton($"list.search.filter.in.{(value.Length == 0 ? "folder" : value)}");
            b.Click += (_, _) => _ = _model.SetOperatorAsync("in", v);
            PlaceChoices.Children.Add(b);
            _places.Add((value, b));
        }
    }

    // Chip (search-filter-panel.tsx:259-274): inline-flex gap-1.5 h-7 px-2.5 rounded-full text-xs border; on: bg-primary/10 border-primary/40 text-primary with a check in place of the icon; off: bg-background border-border
    // text-muted-foreground.
    private static Button PillButton(string id)
    {
        var b = new Button { Height = 28, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Style = StyleOf("Button.Chip") };
        AutomationProperties.SetAutomationId(b, id);
        return b;
    }

    private static void Pill(Button b, string label, string? icon, bool on)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (on)
        {
            row.Children.Add(new SIcon { Kind = "Check", Size = 14, StrokeWidth = 2 });
        }
        else if (icon is not null)
        {
            row.Children.Add(new SIcon { Kind = icon, Size = 14, StrokeWidth = 2 });
        }

        row.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
        b.Content = row;
        b.Background = on ? Brush("FeedbackActive") : Brush("Bg");
        b.BorderBrush = on ? Brush("Accent") : Brush("Line");
        b.Foreground = on ? Brush("FeedbackActiveFg") : Brush("Dim");
        AutomationProperties.SetName(b, label);
        AutomationProperties.SetItemStatus(b, on ? "on" : "off");
    }

    // Typing in a panel field searches live after a short pause (FilterChangeMode "debounced", search-filter-panel.tsx:92).
    private void OnFieldChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _fieldTimer.Stop();
        _fieldTimer.Start();
    }

    private async Task RunFieldsAsync()
    {
        foreach (var (key, box) in new[] { ("from", FromBox), ("to", ToBox), ("subject", SubjectBox) })
        {
            if (!string.Equals(box.Text.Trim(), _model.GetOperator(key), StringComparison.Ordinal))
            {
                var v = box.Text.Trim();
                await _model.SetOperatorAsync(key, v.Any(char.IsWhiteSpace) ? $"\"{v}\"" : v);
            }
        }
    }

    // Enter in a field submits the form: the pending search runs and the panel closes (onDone).
#pragma warning disable VSTHRD100 // XAML event handlers must return void; the awaited call reports its own failure through the ViewModel
    private async void OnFieldKeyDown(object sender, KeyRoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        if (e.Key == global::Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            _fieldTimer.Stop();
            await RunFieldsAsync();
            _model.FilterOpen = false;
        }
    }

    private void OnDoneClick(object sender, RoutedEventArgs e)
    {
        _fieldTimer.Stop();
        _ = RunFieldsAsync();
        _model.FilterOpen = false;
    }

    private void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        _fieldTimer.Stop();
        FromBox.Text = string.Empty;
        ToBox.Text = string.Empty;
        SubjectBox.Text = string.Empty;
        _ = _model.RemoveChipAsync(string.Empty);
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        // TextChanged for a programmatic set arrives after the guard is down: text the model already holds is not an edit (no dropdown opens when a folder or a clear rewrites the box)
        if (_syncing || string.Equals(SearchBox.Text, _model.Text, StringComparison.Ordinal))
        {
            return;
        }

        _model.Edited(SearchBox.Text);
    }

    private void OnBoxFocused(object sender, RoutedEventArgs e)
    {
        _focused = true;
        ApplyFrame();
        _model.Edited(SearchBox.Text);
    }

    // search-box.tsx:156-163: leaving the field closes the dropdown
    private void OnBoxLostFocus(object sender, RoutedEventArgs e)
    {
        _focused = false;
        ApplyFrame();
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, _model.Dismiss);
    }

    private void OnClearClick(object sender, RoutedEventArgs e) => _ = _model.ClearAsync();

    private void OnFilterClick(object sender, RoutedEventArgs e) => _model.FilterOpen = !_model.FilterOpen;

#pragma warning disable VSTHRD100 // XAML event handlers must return void; the awaited call reports its own failure through the ViewModel
    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        switch (e.Key)
        {
            case global::Windows.System.VirtualKey.Enter:
                e.Handled = true;
                if (!await _model.AcceptAsync())
                {
                    _model.Text = SearchBox.Text;
                    await _model.SearchAsync();
                }

                break;
            case global::Windows.System.VirtualKey.Down:
                e.Handled = _model.MoveActive(1);
                break;
            case global::Windows.System.VirtualKey.Up:
                e.Handled = _model.MoveActive(-1);
                break;
            case global::Windows.System.VirtualKey.Escape:
                e.Handled = true;
                if (_model.SuggestionsOpen)
                {
                    _model.Dismiss();
                }
                else if (SearchBox.Text.Length > 0)
                {
                    await _model.ClearAsync();
                }

                break;
            default:
                break;
        }
    }
}
