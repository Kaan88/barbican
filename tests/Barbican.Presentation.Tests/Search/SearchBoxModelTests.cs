// M2 exit tests of stream search (plan 6.4): typing, Enter, empty Enter, the clear X, suggestions, chips (removing one rewrites the text and searches again), the filter panel's tokens. I-272..I-280.

using Barbican.Core.Search;
using Barbican.Presentation.Search;

namespace Barbican.Presentation.Tests.Search;

public sealed class SearchBoxModelTests
{
    private readonly List<string> _ran = [];

    private SearchBoxModel Model(SearchHistoryStore? history = null, params (string, string)[] people) =>
        new(t =>
        {
            _ran.Add(t);
            return Task.CompletedTask;
        }, history ?? new SearchHistoryStore(null), () => people);

    [Fact]
    public async Task Enter_runs_the_text_and_shows_its_chips()
    {
        var m = Model();
        m.Edited("from:alice has:attachment invoice");
        await m.SearchAsync();
        Assert.Equal(["from:alice has:attachment invoice"], _ran);
        Assert.Equal(["from", "has"], m.Chips.Select(c => c.Key).ToArray());
        Assert.Equal(2, m.FilterCount);
        Assert.False(m.SuggestionsOpen);
    }

    [Fact]
    public async Task Removing_a_chip_rewrites_the_text_and_searches_again()
    {
        var m = Model();
        m.Edited("from:alice has:attachment invoice");
        await m.SearchAsync();
        await m.RemoveChipAsync("from");
        Assert.Equal("has:attachment invoice", m.Text);
        Assert.Equal("has:attachment invoice", _ran[^1]);
        Assert.Equal(["has"], m.Chips.Select(c => c.Key).ToArray());

        // Clear all takes every operator out and keeps the words
        await m.RemoveChipAsync(string.Empty);
        Assert.Equal("invoice", m.Text);
        Assert.Empty(m.Chips);
    }

    [Fact]
    public async Task An_empty_enter_goes_back_to_the_folder_and_remembers_nothing()
    {
        var h = new SearchHistoryStore(null);
        var m = Model(h);
        m.Edited("   ");
        await m.SearchAsync();
        Assert.Equal(["   "], _ran);
        Assert.Empty(h.Load());
        await m.ClearAsync();
        Assert.Equal(string.Empty, m.Text);
    }

    [Fact]
    public async Task Suggestions_are_recent_searches_then_people_and_a_pick_runs_it()
    {
        var h = new SearchHistoryStore(null);
        h.Add("invoice march");
        h.Add("travel");
        var m = Model(h, ("Ann Lee", "ann@x.example"));

        // the focused empty box: recent searches
        m.Edited(string.Empty);
        Assert.Equal([new SearchSuggestion.Recent("travel"), new SearchSuggestion.Recent("invoice march")], m.Suggestions.ToArray());
        Assert.True(m.SuggestionsOpen);

        // typing: matching recent searches, then people
        m.Edited("ann");
        Assert.Equal(new SearchSuggestion.Person("Ann Lee", "ann@x.example"), m.Suggestions[0]);
        Assert.True(m.MoveActive(1));
        Assert.True(await m.AcceptAsync());
        Assert.Equal("from:ann@x.example", _ran[^1]);
        Assert.Equal("from:ann@x.example", h.Load()[0]);
        Assert.False(m.SuggestionsOpen);

        // nothing highlighted: Enter is a plain search
        m.Edited("budget");
        Assert.False(await m.AcceptAsync());
    }

    [Fact]
    public async Task The_filter_panel_edits_operators_and_searches_live()
    {
        var m = Model();
        m.Edited("report");
        await m.ToggleTokenAsync("is:unread", "is:read");
        Assert.Equal("report is:unread", m.Text);
        Assert.True(m.HasToken("IS:UNREAD"));
        await m.ToggleTokenAsync("is:read", "is:unread");
        Assert.Equal("report is:read", m.Text);
        await m.ToggleTokenAsync("is:read", "is:unread");
        Assert.Equal("report", m.Text);
        await m.SetOperatorAsync("after", "2026-01-02");
        await m.SetOperatorAsync("in", "trash");
        Assert.Equal("report after:2026-01-02 in:trash", m.Text);
        await m.SetOperatorAsync("after", string.Empty);
        Assert.Equal("report in:trash", m.Text);
        Assert.Equal(6, _ran.Count);
    }

    [Fact]
    public async Task The_panel_sets_has_the_words_and_both_size_bounds_with_one_search_each()
    {
        var m = Model();
        m.Edited("report");
        await m.SetOperatorAsync("body", "\"q3 report\"");
        Assert.Equal("report body:\"q3 report\"", m.Text);
        Assert.Equal("q3 report", m.GetOperator("body"));
        Assert.Equal(new SearchChip("body", "Body", "q3 report"), m.Chips[0]);

        // a preset sets the minimum and clears the maximum in one search (search-filter-panel.tsx:184-199)
        var before = _ran.Count;
        await m.SetOperatorsAsync(("larger", "1024"), ("smaller", string.Empty));
        Assert.Equal("report body:\"q3 report\" larger:1024", m.Text);
        Assert.Equal(before + 1, _ran.Count);
        Assert.Equal(["body", "larger"], m.Chips.Select(c => c.Key).ToArray());
        Assert.Equal(2, m.FilterCount);

        // both custom bounds: one search for the pair, and each chip is removable
        before = _ran.Count;
        await m.SetOperatorsAsync(("larger", "100"), ("smaller", "2048"));
        Assert.Equal("report body:\"q3 report\" larger:100 smaller:2048", m.Text);
        Assert.Equal(before + 1, _ran.Count);
        Assert.Equal([new SearchChip("larger", "Larger than (KB)", "100 KB")], m.Chips.Where(c => string.Equals(c.Key, "larger", StringComparison.Ordinal)).ToArray());
        await m.RemoveChipAsync("smaller");
        Assert.Equal("report body:\"q3 report\" larger:100", m.Text);

        // an empty value takes the operator out
        await m.SetOperatorAsync("body", string.Empty);
        Assert.Equal("report larger:100", m.Text);
    }

    [Fact]
    public void The_list_moving_the_text_updates_the_box_and_the_chips_without_a_search()
    {
        var m = Model();
        m.Sync("is:starred");
        Assert.Equal("is:starred", m.Text);
        Assert.Single(m.Chips);
        m.Sync(string.Empty);
        Assert.Empty(m.Chips);
        Assert.Empty(_ran);
    }

    [Fact]
    public async Task The_to_button_of_a_person_searches_the_recipient_and_the_panel_reads_operators_back()
    {
        var m = Model(null, ("Alice Smith", "alice@example.com"));
        m.Edited("ali");
        Assert.True(m.SuggestionsOpen);
        await m.PickPersonToAsync(0);
        Assert.Equal("to:alice@example.com", _ran[^1]);
        Assert.Equal("alice@example.com", m.GetOperator("to"));
        Assert.Equal(string.Empty, m.GetOperator("from"));
    }
}
