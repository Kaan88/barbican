// Mirrors: Rust contacts_x.rs:121 fields_of, :190 build_card, :94 date_text, :107 parse_date_text; Bulwark components/contacts/contact-form.tsx (entries kept with their unknown properties); B-CON-011, B-CON-012. Contract from the M5 plan section 4.1; owner con-data.

#pragma warning disable MA0051 // one block per entry kind of the form, in display order

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Contacts;

/// <summary>The form rows of a JSContact card and the card a saved form builds.</summary>
public static partial class ContactFields
{
    /// <summary>The entry rows of a card (emails, phones, addresses, dates, related people, links, messaging, ...), each remembering its key so a save keeps what the form does not show.</summary>
    public static IReadOnlyList<ContactField> Of(JsonNode card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var rows = new List<ContactField>();
        static string? LabelOf(JsonNode e) => e.Str("label") is { } l && l.Trim().Length > 0 ? l : null;
        var contexts = ContactOptions.Contexts.Count;

        foreach (var (key, e) in ContactJson.Entries(card, "emails"))
        {
            var label = LabelOf(e);
            rows.Add(new ContactField { Section = "email", Key = key, Kind = label is null ? ContextIndex(ContactJson.Get(e, "contexts")) : contexts, A = e.Str("address") ?? string.Empty, Label = label });
        }

        foreach (var (key, p) in ContactJson.Entries(card, "phones"))
        {
            var ctx = ContactJson.Get(p, "contexts");
            rows.Add(new ContactField
            {
                Section = "phone",
                Key = key,
                Kind = PhoneKind(p),
                Kind2 = ContactJson.IsTrue(ContactJson.Get(ctx, "private")) ? 1 : ContactJson.IsTrue(ContactJson.Get(ctx, "work")) ? 2 : 0,
                A = p.Str("number") ?? string.Empty,
                Label = LabelOf(p),
            });
        }

        foreach (var (key, a) in ContactJson.Entries(card, "addresses"))
        {
            var parts = VCardParser.AddressPartsOf(a);
            var street = parts.Street.Length == 0 && parts.Locality.Length == 0 && parts.Region.Length == 0 && parts.Postcode.Length == 0 && parts.Country.Length == 0
                ? a.Str("full") ?? a.Str("fullAddress") ?? string.Empty
                : parts.Street;
            var label = LabelOf(a);
            rows.Add(new ContactField { Section = "address", Key = key, Kind = label is null ? ContextIndex(ContactJson.Get(a, "contexts")) : contexts, A = street, B = parts.Locality, C = parts.Region, D = parts.Postcode, E = parts.Country, Label = label });
        }

        foreach (var (key, u) in ContactJson.Entries(card, "links"))
        {
            var label = LabelOf(u);
            rows.Add(new ContactField { Section = "url", Key = key, Kind = label is null ? ContextIndex(ContactJson.Get(u, "contexts")) : contexts, A = u.Str("uri") ?? string.Empty, Label = label });
        }

        foreach (var (key, s) in ContactJson.Entries(card, "onlineServices"))
        {
            var label = LabelOf(s);

            // a plain web link without a service is a website (Bulwark keeps URLs here too)
            var value = s.Str("user") ?? s.Str("uri") ?? string.Empty;
            rows.Add(new ContactField { Section = "im", Key = key, Kind = label is null ? ContextIndex(ContactJson.Get(s, "contexts")) : contexts, A = value, B = s.Str("service") ?? string.Empty, Label = label });
        }

        foreach (var (key, a) in ContactJson.Entries(card, "anniversaries"))
        {
            var at = IndexOf(ContactOptions.DateKinds, a.Str("kind"));
            rows.Add(new ContactField { Section = "date", Key = key, Kind = at < 0 ? 3 : at, A = ContactJson.Get(a, "date") is { } d ? DateText(d) : string.Empty });
        }

        foreach (var (name, r) in ContactJson.Entries(card, "relatedTo"))
        {
            var at = IndexOf(ContactOptions.Relations, ContactJson.FirstTrueKey(ContactJson.Get(r, "relation")));
            rows.Add(new ContactField { Section = "related", Key = name, Kind = at < 0 ? 0 : at, A = name });
        }

        return rows;
    }

    /// <summary>The single-value fields of a card for the form: the name parts (a card with only a full name puts it in the first name), nickname, the first organisation and its first unit, the title and the role, the first note.</summary>
    public static ContactForm FormOf(JsonNode? card)
    {
        var comps = ContactJson.Get(ContactJson.Get(card, "name"), "components") as JsonArray ?? [];
        string Comp(params string[] kinds) => comps.Select(x => (Kind: x.Str("kind"), Value: x.Str("value"))).FirstOrDefault(x => x.Kind is not null && kinds.Contains(x.Kind, StringComparer.Ordinal)).Value ?? string.Empty;
        var given = Comp("given");
        var surname = Comp("surname");
        if (given.Length == 0 && surname.Length == 0 && comps.Count == 0)
        {
            given = ContactJson.Get(card, "name").Str("full") ?? string.Empty;
        }

        var org = ContactJson.Entries(card, "organizations").Select(e => e.Value).FirstOrDefault();
        var titles = ContactJson.Values(card, "titles");
        return new ContactForm
        {
            Prefix = Comp("title", "prefix"),
            Given = given,
            Middle = Comp("given2", "middle", "additional"),
            Surname = surname,
            Suffix = Comp("generation", "suffix"),
            Nickname = ContactJson.Values(card, "nicknames").Select(n => n.Str("name")).FirstOrDefault() ?? string.Empty,
            Org = org.Str("name") ?? string.Empty,
            Dept = ContactJson.Get(org, "units") is JsonArray { Count: > 0 } units ? units[0].Str("name") ?? string.Empty : string.Empty,
            JobTitle = titles.Where(t => string.Equals(t.Str("kind") ?? "title", "title", StringComparison.Ordinal)).Select(t => t.Str("name")).FirstOrDefault() ?? string.Empty,
            Role = titles.Where(t => string.Equals(t.Str("kind"), "role", StringComparison.Ordinal)).Select(t => t.Str("name")).FirstOrDefault() ?? string.Empty,
            Notes = ContactJson.Values(card, "notes").Select(n => n.Str("note")).FirstOrDefault() ?? string.Empty,
        };
    }

    /// <summary>A JSContact PartialDate as the form shows it ("1815-12-10", "--06-01").</summary>
    public static string DateText(JsonNode date)
    {
        ArgumentNullException.ThrowIfNull(date);
        if (date.AsString() is { } text)
        {
            return text;
        }

        if (date.Str("utc") is { } utc)
        {
            var cut = utc.IndexOf('T', StringComparison.Ordinal);
            return cut < 0 ? utc : utc[..cut];
        }

        var y = ContactJson.Int(ContactJson.Get(date, "year"));
        var m = ContactJson.Int(ContactJson.Get(date, "month"));
        var d = ContactJson.Int(ContactJson.Get(date, "day"));
        static string Pad(long v, int width) => v.ToString("D" + width.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return (y, m, d) switch
        {
            ({ } yy, { } mm, { } dd) => $"{Pad(yy, 4)}-{Pad(mm, 2)}-{Pad(dd, 2)}",
            (null, { } mm, { } dd) => $"--{Pad(mm, 2)}-{Pad(dd, 2)}",
            ({ } yy, { } mm, null) => $"{Pad(yy, 4)}-{Pad(mm, 2)}",
            ({ } yy, null, null) => yy.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty,
        };
    }

    /// <summary>The form's date text (yyyy-MM-dd, --MM-dd, yyyy-MM) as a PartialDate object; a ValidationError for anything else ("31/12").</summary>
    public static Result<JsonObject> ParseDateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var t = text.Trim();
        var p = new Dictionary<string, string>(StringComparer.Ordinal) { ["VALUE"] = "date" };
        return VCardParser.ParseDate(t, p) is JsonObject date
            ? Result<JsonObject>.Ok(date)
            : Result<JsonObject>.Fail(new ValidationError($"\"{t}\" is not a date (use YYYY-MM-DD or --MM-DD)"));
    }

    /// <summary>Private is 0, work 1, anything else 2 (the order of <see cref="ContactOptions.Contexts"/> as the form lists it).</summary>
    internal static int ContextIndex(JsonNode? contexts) => ContactJson.IsTrue(ContactJson.Get(contexts, "private")) ? 0 : ContactJson.IsTrue(ContactJson.Get(contexts, "work")) ? 1 : 2;

    /// <summary>The phone's type as an index of <see cref="ContactOptions.PhoneFeatures"/>; its own label is the entry after the last one.</summary>
    internal static int PhoneKind(JsonNode p)
    {
        if (p.Str("label") is { } label && label.Trim().Length > 0)
        {
            return ContactOptions.PhoneFeatures.Count;
        }

        var f = ContactJson.Get(p, "features");
        if (ContactJson.IsTrue(ContactJson.Get(f, "mobile")) || ContactJson.IsTrue(ContactJson.Get(f, "cell")))
        {
            return 1;
        }

        for (var i = 0; i < ContactOptions.PhoneFeatures.Count; i++)
        {
            if (ContactJson.IsTrue(ContactJson.Get(f, ContactOptions.PhoneFeatures[i].Key)))
            {
                return i;
            }
        }

        return 0;
    }

    private static int IndexOf(IReadOnlyList<(string Key, string Label)> options, string? key)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].Key, key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
