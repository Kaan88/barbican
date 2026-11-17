// Mirrors: Bulwark components/search/ (search-box.tsx, search-suggestions.tsx, search-chips.tsx, search-filter-panel.tsx); Rust search_ui.rs. I-023, I-146 (Enter searches, Escape clears), I-272 (type, Enter, empty Enter),
// I-273 (clear X), I-274 (suggestions), I-275 (chips), I-276 (filter toggle with count), I-277..I-280 (filter panel: live search, presets, read filter, starred, attachment, "Search in", Clear all).
// The model (SearchBoxModel) holds the behaviour; this view only draws it: chips, suggestions and the panel are rebuilt from the model when it changes, and the field's own text is never rewritten while the
// user types (no caret jump, no flicker).

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Core.Search;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Search;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Views.Search;

/// <summary>The search box of the message list.</summary>
public sealed partial class SearchBoxView : UserControl
{
    private readonly MessageListViewModel _list;
    private readonly SearchBoxModel _model;
    private bool _syncing;

    public SearchBoxView()
    {
        _list = ViewHost.Get<MessageListViewModel>();
        _model = ViewHost.Get<SearchBoxModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        _list.FocusSearchRequested += (_, _) => SearchBox.Focus(FocusState.Programmatic);
        _model.PropertyChanged += (_, _) => Redraw();
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

    private static Brush Brush(string key) => Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b ? b : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    private static Style? StyleOf(string key) => Application.Current.Resources.TryGetValue(key, out var v) ? v as Style : null;

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
        FilterCount.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DrawChips();
        DrawSuggestions();
        DrawFilterPanel();
    }

    private void DrawChips()
    {
        var chips = _model.Chips;
        ChipsScroller.Visibility = chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ChipsPanel.Children.Clear();
        foreach (var chip in chips)
        {
            var label = chip.Value.Length > 0 ? $"{chip.Label}: {chip.Value}" : chip.Label;
            var b = Chip($"{label}  ✕", () => _ = _model.RemoveChipAsync(chip.Key), $"list.search.chip.{chip.Key}");
            ChipsPanel.Children.Add(b);
        }

        if (chips.Count >= 2)
        {
            ChipsPanel.Children.Add(Chip("Clear all", () => _ = _model.RemoveChipAsync(string.Empty), "list.search.clear-all"));
        }
    }

    private static Button Chip(string text, Action click, string id)
    {
        var b = new Button { Content = text, Style = StyleOf("Button.Chip"), Padding = new Thickness(10, 3, 10, 3) };
        AutomationProperties.SetAutomationId(b, id);
        AutomationProperties.SetName(b, text);
        b.Click += (_, _) => click();
        return b;
    }

    private void DrawSuggestions()
    {
        SuggestList.Children.Clear();
        SuggestPanel.Visibility = _model.SuggestionsOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!_model.SuggestionsOpen)
        {
            return;
        }

        string? heading = null;
        for (var i = 0; i < _model.Suggestions.Count; i++)
        {
            var index = i;
            var (group, label, sub, removable) = _model.Suggestions[i] switch
            {
                SearchSuggestion.Recent r => ("Recent searches", r.Query, string.Empty, true),
                SearchSuggestion.Person p => ("People", $"Search mail from {p.Email}", p.Name, false),
                _ => (string.Empty, string.Empty, string.Empty, false),
            };
            if (!string.Equals(group, heading, StringComparison.Ordinal))
            {
                heading = group;
                SuggestList.Children.Add(new TextBlock { Text = group, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = Brush("Faint"), Margin = new Thickness(8, 6, 0, 2) });
            }

            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
            var text = new StackPanel { Spacing = 0, Margin = new Thickness(8, 4, 8, 4) };
            text.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = Brush("Text"), TextTrimming = TextTrimming.CharacterEllipsis });
            if (sub.Length > 0)
            {
                text.Children.Add(new TextBlock { Text = sub, FontSize = 11.5, Foreground = Brush("Faint"), TextTrimming = TextTrimming.CharacterEllipsis });
            }

            var pick = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Style = StyleOf("Button.Icon"), Padding = new Thickness(0) };
            pick.Click += (_, _) => _ = _model.PickAsync(index);
            AutomationProperties.SetAutomationId(pick, $"list.search.suggestion.{i}");
            AutomationProperties.SetName(pick, label);
            row.Children.Add(pick);
            if (removable)
            {
                var x = new Button { Content = "✕", Style = StyleOf("Button.Icon"), Width = 24, Height = 24, FontSize = 11 };
                x.Click += (_, _) => _model.RemoveRecent(index);
                AutomationProperties.SetName(x, "Remove from recent searches");
                Grid.SetColumn(x, 1);
                row.Children.Add(x);
            }

            if (i == _model.Active)
            {
                row.Background = Brush("Hover");
            }

            SuggestList.Children.Add(row);
        }
    }

    private void DrawFilterPanel()
    {
        FilterPanel.Visibility = _model.FilterOpen ? Visibility.Visible : Visibility.Collapsed;
        FilterRows.Children.Clear();
        if (!_model.FilterOpen)
        {
            return;
        }

        FilterRows.Children.Add(Group("Read", [
            Toggle("Unread", "is:unread", "is:read"),
            Toggle("Read", "is:read", "is:unread"),
        ]));
        FilterRows.Children.Add(Group("Starred", [
            Toggle("Starred", "is:starred", "is:unstarred"),
            Toggle("Not starred", "is:unstarred", "is:starred"),
        ]));
        FilterRows.Children.Add(Group("Attachments", [Toggle("Has attachment", "has:attachment")]));
        FilterRows.Children.Add(Group("Date", [
            Preset("Past week", "after", DateTime.Today.AddDays(-7)),
            Preset("Past month", "after", DateTime.Today.AddMonths(-1)),
            Preset("Past year", "after", DateTime.Today.AddYears(-1)),
            Preset("Any time", "after", null),
        ]));
        FilterRows.Children.Add(Group("Search in", [
            Place("This folder", string.Empty),
            Place("Trash", "trash"),
            Place("Spam", "spam"),
        ]));
        FilterRows.Children.Add(Chip("Clear all", () => _ = _model.RemoveChipAsync(string.Empty), "list.search.filter.clear"));
    }

    private static StackPanel Group(string title, IEnumerable<Button> buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock { Text = title, FontSize = 12, Foreground = Brush("Faint"), VerticalAlignment = VerticalAlignment.Center, MinWidth = 76 });
        foreach (var b in buttons)
        {
            row.Children.Add(b);
        }

        return row;
    }

    private Button Toggle(string label, string token, params string[] excludes)
    {
        var on = _model.HasToken(token);
        var b = Chip(on ? $"{label} ✓" : label, () => _ = _model.ToggleTokenAsync(token, excludes), $"list.search.filter.{token}");
        return b;
    }

    private Button Preset(string label, string key, DateTime? day) =>
        Chip(label, () => _ = _model.SetOperatorAsync(key, day is { } d ? d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : string.Empty), $"list.search.filter.{key}.{label}");

    private Button Place(string label, string value) =>
        Chip(label, () => _ = _model.SetOperatorAsync("in", value), $"list.search.filter.in.{label}");

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _model.Edited(SearchBox.Text);
    }

    private void OnBoxFocused(object sender, RoutedEventArgs e) => _model.Edited(SearchBox.Text);

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
