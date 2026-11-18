// Ported from Rust src/cal_editor.rs test :1672 people_parsing (T-042) and src/cal_extra.rs test :1077 people_search (T-052). The contact cards are an argument, not a thread-local (D-M4-06). M4 stream cal-edit.

using Barbican.Core.Calendar.Editing;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Editing;

public sealed class CalendarPeopleTests
{
    [Fact]
    [Red("cal-edit")]
    [RustTest("cal_editor::people_parsing")]
    public void People_parsing()
    {
        Assert.Equal(new CalendarPerson("Ann Lee", "ann@x.example"), CalendarPeople.Parse("Ann Lee <ann@x.example>"));
        Assert.Equal(new CalendarPerson(string.Empty, "bob@y.example"), CalendarPeople.Parse(" bob@y.example "));
        Assert.Null(CalendarPeople.Parse("not an address"));
        Assert.Null(CalendarPeople.Parse("a@b"));
        Assert.Equal("AL", CalendarPeople.Initials("Ann Lee", "x"));
        Assert.Equal("BS", CalendarPeople.Initials(string.Empty, "bob.smith@y.example"));
    }

    [Fact]
    [Red("cal-edit")]
    [RustTest("cal_extra::people_search")]
    public void People_search()
    {
        var cards = new[]
        {
            J("""{"id": "1", "uid": "u1", "name": {"full": "Ann Lee"}, "emails": {"e": {"address": "ann@x.example"}}}"""),
            J("""{"id": "2", "uid": "u2", "name": {"full": "Bob Annex"}, "emails": {"e": {"address": "bob@x.example"}}}"""),
            J("""{"id": "3", "uid": "g", "kind": "group", "name": {"full": "Annual team"}, "members": {"u1": true, "u2": true}}"""),
        };
        var hits = CalendarPeople.Matching(cards, "ann", [], 8);
        Assert.Equal("Ann Lee", hits[0].Person.Name);
        Assert.Contains(hits, h => string.Equals(h.Person.Name, "Annual team", StringComparison.Ordinal) && h.Members.Count == 2);
        Assert.All(CalendarPeople.Matching(cards, "ann", ["ann@x.example"], 8), h => Assert.False(string.Equals("ann@x.example", h.Person.Email, StringComparison.Ordinal)));
    }
}
