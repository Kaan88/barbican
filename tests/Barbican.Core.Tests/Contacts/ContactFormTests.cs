// The form helpers con-ui reads (D-M5-A05): the single fields of a card for the form, the subtitle, the day "today" and the detail lines' actions.

#pragma warning disable MA0006, MA0002

using Barbican.Core.Contacts;
using static Barbican.Core.Tests.Contacts.ContactTestData;

namespace Barbican.Core.Tests.Contacts;

public sealed class ContactFormTests
{
    private static System.Text.Json.Nodes.JsonNode Card() => J("""
        {"name": {"components": [{"kind": "title", "value": "Countess"}, {"kind": "given", "value": "Ada"}, {"kind": "given2", "value": "King"}, {"kind": "surname", "value": "Lovelace"}], "full": "Countess Ada King Lovelace"},
         "nicknames": {"k0": {"name": "Enchantress"}}, "organizations": {"o0": {"name": "Analytical Engines", "units": [{"name": "R&D"}, {"name": "Maths"}]}},
         "titles": {"t0": {"name": "Programmer", "kind": "title"}, "t1": {"name": "Founder", "kind": "role"}}, "notes": {"n0": {"note": "First"}},
         "emails": {"e0": {"address": "ada@x.example"}}, "links": {"l0": {"uri": "https://x.example"}}, "onlineServices": {"s0": {"user": "ada", "service": "Matrix"}}}
        """);

    [Fact]
    public void The_form_reads_the_single_fields_of_a_card()
    {
        var f = ContactFields.FormOf(Card());
        Assert.Equal("Countess", f.Prefix);
        Assert.Equal("Ada", f.Given);
        Assert.Equal("King", f.Middle);
        Assert.Equal("Lovelace", f.Surname);
        Assert.Equal("Enchantress", f.Nickname);
        Assert.Equal("Analytical Engines", f.Org);
        Assert.Equal("R&D", f.Dept);
        Assert.Equal("Programmer", f.JobTitle);
        Assert.Equal("Founder", f.Role);
        Assert.Equal("First", f.Notes);
        Assert.Equal("Anna Müller", ContactFields.FormOf(J("""{"name": {"full": "Anna Müller"}}""")).Given);
        Assert.Equal(string.Empty, ContactFields.FormOf(null).Given);
    }

    [Fact]
    public void A_save_without_edits_changes_nothing_and_an_emptied_department_removes_only_the_first_unit()
    {
        var card = Card();
        var form = ContactFields.FormOf(card);
        var same = ContactFields.Build(card, form, ContactFields.Of(card)).Value!;
        Assert.Equal(2, same["organizations"]!["o0"]!["units"]!.AsArray().Count);
        Assert.Equal("ada", (string?)same["onlineServices"]!["s0"]!["user"]);
        var cleared = ContactFields.Build(card, form with { Dept = "" }, ContactFields.Of(card)).Value!;
        var left = cleared["organizations"]!["o0"]!["units"]!.AsArray();
        Assert.Equal("Maths", (string?)Assert.Single(left)!["name"]);
        Assert.Equal("Analytical Engines", (string?)cleared["organizations"]!["o0"]!["name"]);
    }

    [Fact]
    public void The_subtitle_joins_title_role_and_organisation()
    {
        Assert.Equal("Programmer · Founder · Analytical Engines, R&D, Maths", ContactDetail.Subtitle(Card()));
    }

    [Fact]
    public void Detail_lines_carry_their_action_and_a_near_birthday_its_tint()
    {
        var lines = ContactDetail.Lines(J("""{"emails": {"e": {"address": "a@x.example"}}, "links": {"l": {"uri": "https://x.example"}}, "anniversaries": {"b": {"kind": "birth", "date": {"month": 11, "day": 20}}, "d": {"kind": "death", "date": {"month": 11, "day": 20}}}}"""), new DateOnly(2026, 11, 16));
        Assert.Equal("mail", lines.First(l => l.Section == "EMAIL").Action);
        Assert.Equal("url", lines.First(l => l.Section == "WEB").Action);
        var dates = lines.Where(l => l.Section == "DATES").ToList();
        Assert.Equal("rose", dates[0].Tint);
        Assert.Equal(string.Empty, dates[1].Tint);
    }

    [Fact]
    public void Today_is_the_day_in_the_settings_zone()
    {
        var now = new DateTimeOffset(2026, 11, 18, 23, 30, 0, TimeSpan.Zero);
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(now);
        Assert.Equal(new DateOnly(2026, 11, 18), ContactDates.Today(time, "UTC"));
        Assert.Equal(new DateOnly(2026, 11, 19), ContactDates.Today(time, "Pacific/Auckland"));
    }

    [Fact]
    public void A_date_the_year_lacks_a_leap_day_falls_on_the_first_of_march_and_text_is_not_a_date()
    {
        Assert.Equal(new DateOnly(2027, 3, 1).DayNumber - new DateOnly(2026, 11, 16).DayNumber, ContactDates.Pretty(J("""{"month": 2, "day": 29}"""), new DateOnly(2026, 11, 16)).DaysUntilNext);
        Assert.Null(ContactDates.Pretty(J("\"circa 1900\""), new DateOnly(2026, 11, 16)).DaysUntilNext);
        Assert.Equal("1985", ContactDates.Pretty(J("""{"year": 1985}"""), new DateOnly(2026, 1, 1)).Text);
    }
}
