// Ported from Rust src/search_history.rs tests (:89-:130). Red until stream search implements SearchHistory (M2 plan 6.5, T-256..T-258).

using Barbican.Core.Search;

namespace Barbican.Core.Tests.Search;

public sealed class SearchHistoryTests
{
    [Fact]
    [Red("search")]
    [RustTest("search_history::adds_newest_first_without_duplicates")]
    public void Adds_newest_first_without_duplicates()
    {
        var v = SearchHistory.WithAdded([], "  budget ");
        Assert.Equal(["budget"], v);
        v = SearchHistory.WithAdded(v, "report");
        v = SearchHistory.WithAdded(v, "BUDGET");
        Assert.Equal(["BUDGET", "report"], v);
        for (var i = 0; i < 20; i++)
        {
            v = SearchHistory.WithAdded(v, $"q{i}");
        }

        Assert.Equal(SearchHistory.MaxRecent, v.Count);
        Assert.Equal("q19", v[0]);
        Assert.Equal(v, SearchHistory.WithAdded(v, "   "));
    }

    [Fact]
    [Red("search")]
    [RustTest("search_history::suggestions_like_bulwark")]
    public void Suggestions_like_bulwark()
    {
        string[] recent = ["invoice march", "invoice", "travel", "x1", "x2", "x3"];

        // empty box: the five newest
        var s = SearchHistory.Build(string.Empty, recent, []);
        Assert.Equal(5, s.Count);
        Assert.Equal(new SearchSuggestion.Recent("invoice march"), s[0]);

        // typing: matching recent searches, not the exact one, then people (deduplicated)
        (string Name, string Email)[] people = [("Ann", "ann@x.example"), ("Ann L", "ANN@x.example"), (string.Empty, string.Empty), ("Inv Co", "billing@inv.example")];
        s = SearchHistory.Build("invoice", recent, people);
        Assert.Equal(new SearchSuggestion.Recent("invoice march"), s[0]);
        Assert.Equal(3, s.Count);
        Assert.Equal(new SearchSuggestion.Person("Ann", "ann@x.example"), s[1]);
    }

    [Fact]
    [Red("search")]
    [RustTest("search_history::people_and_query")]
    public void People_and_query()
    {
        (string Name, string Email)[] c = [("Ann Lee", "ann@x.example"), ("Bob", "bob@y.example"), ("No mail", string.Empty)];
        Assert.Single(SearchHistory.PeopleMatching("lee", c));
        Assert.Equal("Bob", SearchHistory.PeopleMatching("y.example", c)[0].Name);
        Assert.Empty(SearchHistory.PeopleMatching("no", c));
        Assert.Equal("from:ann@x.example", SearchHistory.PersonQuery("ann@x.example"));
    }
}
