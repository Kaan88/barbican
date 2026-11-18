// Ported from Rust src/contacts_x.rs tests :1056 fields_round_trip_keeps_unknown_properties (T-069), :1084 edits_add_remove_and_validate (T-070), :1118 pretty_dates_and_upcoming (T-071); M5 stream con-data.

#pragma warning disable MA0006, MA0002, CA1861 // ported Rust assertions compare string literals and literal arrays

using Barbican.Core.Contacts;
using static Barbican.Core.Tests.Contacts.ContactTestData;

namespace Barbican.Core.Tests.Contacts;

public sealed class ContactFieldsTests
{
    private static System.Text.Json.Nodes.JsonNode Card() => J("""
        {"id": "c1", "name": {"components": [{"kind": "given", "value": "Ada"}, {"kind": "surname", "value": "Lovelace"}, {"kind": "surname2", "value": "Byron"}], "full": "Ada Lovelace"},
         "emails": {"e0": {"address": "ada@x.example", "contexts": {"work": true}, "pref": 1}, "e1": {"address": "ada@home.example", "label": "Old", "x-custom": 7}},
         "phones": {"p0": {"number": "+44 1", "features": {"mobile": true}, "contexts": {"private": true}}},
         "addresses": {"a0": {"components": [{"kind": "name", "value": "1 St James"}, {"kind": "locality", "value": "London"}], "countryCode": "GB"}},
         "anniversaries": {"a0": {"kind": "birth", "date": {"year": 1815, "month": 12, "day": 10}}},
         "relatedTo": {"Charles Babbage": {"relation": {"colleague": true}}},
         "titles": {"t0": {"name": "Countess", "kind": "title"}}, "organizations": {"o0": {"name": "Analytical Engines", "units": [{"name": "R&D"}]}},
         "notes": {"n0": {"note": "First programmer"}}, "nicknames": {"k0": {"name": "Enchantress"}}}
        """);

    [Fact]
    [RustTest("contacts_x::fields_round_trip_keeps_unknown_properties")]
    public void Fields_round_trip_keeps_unknown_properties()
    {
        var c = Card();
        var fields = ContactFields.Of(c);
        Assert.Equal(2, fields.Count(f => f.Section == "email"));
        var e1 = fields.First(f => f.Key == "e1");
        Assert.Equal("Old", e1.Label);
        Assert.Equal(3, e1.Kind);
        var phone = fields.First(f => f.Section == "phone");
        Assert.Equal(1, phone.Kind);
        Assert.Equal(1, phone.Kind2);
        Assert.Equal("1815-12-10", fields.First(f => f.Section == "date").A);
        Assert.Equal(7, fields.First(f => f.Section == "related").Kind);
        var form = new ContactForm { Given = "Ada", Surname = "Lovelace", Org = "Analytical Engines", Dept = "R&D", JobTitle = "Countess", Notes = "First programmer", Nickname = "Enchantress" };
        var built = ContactFields.Build(c, form, fields);
        Assert.True(built.IsOk);
        var o = built.Value!;
        Assert.Equal(1, (int?)o["emails"]!["e0"]!["pref"]);
        Assert.Equal(7, (int?)o["emails"]!["e1"]!["x-custom"]);
        Assert.Equal("Old", (string?)o["emails"]!["e1"]!["label"]);
        Assert.True((bool?)o["emails"]!["e0"]!["contexts"]!["work"]);
        Assert.True((bool?)o["phones"]!["p0"]!["features"]!["mobile"]);
        Assert.Equal("GB", (string?)o["addresses"]!["a0"]!["countryCode"]);
        Assert.Equal(1815, (int?)o["anniversaries"]!["a0"]!["date"]!["year"]);
        Assert.True((bool?)o["relatedTo"]!["Charles Babbage"]!["relation"]!["colleague"]);
        // surname2 survives the name rebuild
        Assert.Contains(o["name"]!["components"]!.AsArray(), x => (string?)x!["kind"] == "surname2");
        Assert.Equal("Ada Lovelace", (string?)o["name"]!["full"]);
        Assert.Equal("R&D", (string?)o["organizations"]!["o0"]!["units"]![0]!["name"]);
    }

    [Fact]
    [RustTest("contacts_x::edits_add_remove_and_validate")]
    public void Edits_add_remove_and_validate()
    {
        var c = Card();
        var fields = ContactFields.Of(c)
            .Where(f => f.Key != "e1")
            .Select(f => f.Key == "e0" ? f with { Kind = 0 } : f.Section == "phone" ? f with { Kind = 2, Kind2 = 2 } : f)
            .ToList();
        fields.Add(new ContactField { Section = "email", A = "new@x.example", Kind = 1 });
        fields.Add(new ContactField { Section = "date", A = "--06-01", Kind = 1 });
        fields.Add(new ContactField { Section = "url", A = "example.org" });
        var form = new ContactForm { Given = "Ada" };
        var o = ContactFields.Build(c, form, fields).Value!;
        Assert.Equal(2, o["emails"]!.AsObject().Count);
        Assert.True((bool?)o["emails"]!["e0"]!["contexts"]!["private"]);
        Assert.Equal("new@x.example", (string?)o["emails"]!["e2"]!["address"]);
        Assert.True((bool?)o["phones"]!["p0"]!["features"]!["fax"]);
        Assert.True((bool?)o["phones"]!["p0"]!["contexts"]!["work"]);
        var wedding = Values(o["anniversaries"]).First(a => (string?)a["kind"] == "wedding");
        Assert.Equal(6, (int?)wedding["date"]!["month"]);
        Assert.Null(wedding["date"]!["year"]);
        Assert.Equal("https://example.org", (string?)o["links"]!["l0"]!["uri"]);
        // emptied single fields go away; titles removed
        Assert.Null(o["titles"]);
        Assert.Null(o["notes"]);
        // bad input is refused
        fields.Add(new ContactField { Section = "email", A = "not an address" });
        Assert.False(ContactFields.Build(c, form, fields).IsOk);
        Assert.False(ContactFields.ParseDateText("31/12").IsOk);
    }

    [Fact]
    [RustTest("contacts_x::pretty_dates_and_upcoming")]
    public void Pretty_dates_and_upcoming()
    {
        var today = new DateOnly(2026, 11, 16);
        var full = ContactDates.Pretty(J("""{"year": 1815, "month": 12, "day": 10}"""), today);
        Assert.Equal("10 December 1815", full.Text);
        Assert.Equal(24, full.DaysUntilNext);
        var noYear = ContactDates.Pretty(J("""{"month": 11, "day": 20}"""), today);
        Assert.Equal("20 November", noYear.Text);
        Assert.Equal(4, noYear.DaysUntilNext);
        Assert.Equal(47, ContactDates.Pretty(J("""{"month": 1, "day": 2}"""), today).DaysUntilNext);
        var lines = ContactDetail.Lines(Card(), today);
        Assert.Contains(lines, l => l.Section == "EMAIL" && l.Label == "Work");
        Assert.Contains(lines, l => l.Section == "PHONE" && l.Label == "Mobile · Personal");
        Assert.Contains(lines, l => l.Section == "ADDRESS" && l.Value.Contains("London", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Section == "RELATED PEOPLE" && l.Label == "Colleague");
    }

    [Fact]
    public void The_fixed_option_lists_are_the_rust_ones()
    {
        Assert.Equal(new[] { "Personal", "Work", "Other" }, ContactOptions.Contexts);
        Assert.Equal(7, ContactOptions.PhoneFeatures.Count);
        Assert.Equal(13, ContactOptions.Relations.Count);
        Assert.Equal("Colleague", ContactOptions.Relations.First(r => r.Key == "colleague").Label);
        Assert.Equal("Anniversary", ContactOptions.DateKinds.First(k => k.Key == "wedding").Label);
    }
}
