// Not Rust rows: Bulwark components/contacts/contact-list.tsx:34-90, :164-243 (the filter panel's predicate and tri-state chips) and contacts-sidebar.tsx:292-317 (the New menu). M6b WP-13, gaps CT-01 and the filters of CT-03.

#pragma warning disable MA0002, MA0006 // ids and names are compared ordinally by nature
using System.Text.Json.Nodes;
using Barbican.Presentation.Contacts;

namespace Barbican.Presentation.Tests.Contacts;

public sealed class ContactsFiltersTests
{
    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    private static readonly JsonNode Anna = J("""
        {"name": {"full": "Anna Müller"}, "emails": {"e": {"address": "anna@acme.example"}}, "phones": {"p": {"number": "+49 30 5550000"}},
         "organizations": {"o": {"name": "ACME Ltd", "units": [{"name": "Research"}]}}, "titles": {"t": {"name": "Chief Engineer"}},
         "addresses": {"a": {"components": [{"kind": "locality", "value": "Berlin"}], "country": "Germany"}},
         "anniversaries": {"b": {"kind": "birth", "date": "1985-03-14"}}}
        """);

    private static readonly JsonNode Bob = J("""{"name": {"full": "Bob"}, "anniversaries": {"b": {"kind": "birth", "date": {"month": 7, "day": 1}}}}""");

    [Fact]
    public void No_filter_matches_everything_and_counts_zero()
    {
        Assert.Equal(0, ContactsFilters.None.ActiveCount);
        Assert.True(ContactsFilters.None.Matches(Anna));
        Assert.True(ContactsFilters.None.Matches(Bob));
    }

    [Fact]
    public void Text_filters_match_substrings_ignoring_case()
    {
        Assert.True((ContactsFilters.None with { Organization = "acme" }).Matches(Anna));
        Assert.True((ContactsFilters.None with { Organization = "research" }).Matches(Anna));
        Assert.False((ContactsFilters.None with { Organization = "acme" }).Matches(Bob));
        Assert.True((ContactsFilters.None with { JobTitle = "engineer" }).Matches(Anna));
        Assert.True((ContactsFilters.None with { Location = "berlin" }).Matches(Anna));
        Assert.True((ContactsFilters.None with { Location = "germany" }).Matches(Anna));
        Assert.False((ContactsFilters.None with { Location = "paris" }).Matches(Anna));
        Assert.True((ContactsFilters.None with { EmailDomain = "@acme" }).Matches(Anna));
        Assert.False((ContactsFilters.None with { EmailDomain = "other" }).Matches(Anna));
    }

    [Fact]
    public void Birthday_month_reads_iso_partial_and_timestamp_dates()
    {
        Assert.True((ContactsFilters.None with { BirthdayMonth = 3 }).Matches(Anna));
        Assert.False((ContactsFilters.None with { BirthdayMonth = 4 }).Matches(Anna));
        Assert.True((ContactsFilters.None with { BirthdayMonth = 7 }).Matches(Bob));
        Assert.True((ContactsFilters.None with { BirthdayMonth = 12 }).Matches(J("""{"anniversaries": {"b": {"kind": "birth", "date": "--12-24"}}}""")));
        Assert.True((ContactsFilters.None with { BirthdayMonth = 5 }).Matches(J("""{"anniversaries": {"b": {"kind": "birth", "date": {"utc": "1990-05-02T00:00:00Z"}}}}""")));
        Assert.False((ContactsFilters.None with { BirthdayMonth = 3 }).Matches(J("""{"anniversaries": {"b": {"kind": "wedding", "date": "1985-03-14"}}}""")));
    }

    [Fact]
    public void Chips_are_tri_state_and_cycle_any_yes_no()
    {
        Assert.Equal(true, ContactsFilters.Cycle(null));
        Assert.Equal(false, ContactsFilters.Cycle(true));
        Assert.Null(ContactsFilters.Cycle(false));
        Assert.True((ContactsFilters.None with { HasEmail = true }).Matches(Anna));
        Assert.False((ContactsFilters.None with { HasEmail = true }).Matches(Bob));
        Assert.True((ContactsFilters.None with { HasPhone = false }).Matches(Bob));
        Assert.False((ContactsFilters.None with { HasPhone = false }).Matches(Anna));
        Assert.True((ContactsFilters.None with { HasPhoto = false }).Matches(Anna));
        Assert.True((ContactsFilters.None with { HasPhoto = true }).Matches(J("""{"media": {"m": {"kind": "photo", "blobId": "B1"}}}""")));
    }

    [Fact]
    public void Active_count_counts_each_set_filter_once()
    {
        var f = ContactsFilters.None with { Organization = " x ", JobTitle = "  ", BirthdayMonth = 2, HasEmail = false };
        Assert.Equal(3, f.ActiveCount);
    }

    [Fact]
    public void The_list_follows_the_filters_and_the_new_menu_lists_four_entries_and_import()
    {
        using var h = new ContactsHarness();
        Assert.Equal(3, h.View.Rows.Count);
        h.View.Filters = ContactsFilters.None with { EmailDomain = "no-such-domain.invalid" };
        Assert.Empty(h.View.Rows);
        h.View.Filters = ContactsFilters.None;
        Assert.Equal(3, h.View.Rows.Count);

        var items = ContactsMenus.New();
        Assert.Equal(["New contact", "New group", "New address book", "Import contacts"], items.Where(i => i.CommandId is { Length: > 0 }).Select(i => i.Label));
        Assert.Equal(ContactsCommandIds.Import, items[^1].CommandId);
    }
}
