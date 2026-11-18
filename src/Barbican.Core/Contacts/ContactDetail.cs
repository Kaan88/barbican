// Mirrors: Rust contacts_x.rs:380 detail_lines, :410 subtitle; Bulwark components/contacts/contact-detail.tsx. B-CON-013. Contract from the M5 plan section 4.1; owner con-data (con-ui draws the lines).

#pragma warning disable MA0051 // one block per section of the detail pane, in display order

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Contacts;

/// <summary>The detail pane's lines of a card, in display order.</summary>
public static class ContactDetail
{
    private static string ContextLabel(JsonNode? c) => ContactJson.IsTrue(ContactJson.Get(c, "work")) ? "Work" : ContactJson.IsTrue(ContactJson.Get(c, "private")) ? "Personal" : string.Empty;

    private static string Label(JsonNode e, string fallback)
    {
        if (e.Str("label") is { } l && l.Trim().Length > 0)
        {
            return l;
        }

        var c = ContextLabel(ContactJson.Get(e, "contexts"));
        return c.Length == 0 ? fallback : c;
    }

    public static IReadOnlyList<ContactLine> Lines(JsonNode card, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(card);
        var v = new List<ContactLine>();
        foreach (var e in ContactJson.Values(card, "emails"))
        {
            v.Add(new ContactLine("EMAIL", Label(e, "Email"), e.Str("address") ?? string.Empty) { Action = "mail" });
        }

        foreach (var p in ContactJson.Values(card, "phones"))
        {
            var kind = ContactFields.PhoneKind(p);
            var feature = kind < ContactOptions.PhoneFeatures.Count ? ContactOptions.PhoneFeatures[kind].Label : string.Empty;
            var label = p.Str("label") is { } l && l.Trim().Length > 0 ? l : string.Join(" · ", new[] { feature, ContextLabel(ContactJson.Get(p, "contexts")) }.Where(x => x.Length > 0));
            v.Add(new ContactLine("PHONE", label, p.Str("number") ?? string.Empty) { Action = "tel" });
        }

        foreach (var a in ContactJson.Values(card, "addresses"))
        {
            var parts = VCardParser.AddressPartsOf(a);
            var city = string.Join(' ', new[] { parts.Postcode, parts.Locality }.Where(x => x.Length > 0));
            var text = string.Join('\n', new[] { parts.Street, city, parts.Region, parts.Country }.Where(x => x.Length > 0));
            if (text.Length == 0)
            {
                text = a.Str("full") ?? a.Str("fullAddress") ?? string.Empty;
            }

            v.Add(new ContactLine("ADDRESS", Label(a, "Address"), text));
        }

        foreach (var u in ContactJson.Values(card, "links"))
        {
            v.Add(new ContactLine("WEB", Label(u, "Website"), u.Str("uri") ?? string.Empty) { Action = "url" });
        }

        foreach (var s in ContactJson.Values(card, "onlineServices"))
        {
            var val = s.Str("user") ?? s.Str("uri") ?? string.Empty;
            var isWeb = s.Str("service") is null && (val.StartsWith("http://", StringComparison.Ordinal) || val.StartsWith("https://", StringComparison.Ordinal));
            var label = s.Str("service") ?? s.Str("label") ?? (isWeb ? "Website" : "Account");
            v.Add(new ContactLine(isWeb ? "WEB" : "MESSAGING & SOCIAL", label, val) { Action = isWeb ? "url" : string.Empty });
        }

        foreach (var a in ContactJson.Values(card, "anniversaries"))
        {
            var kind = ContactOptions.DateKinds.Where(k => string.Equals(k.Key, a.Str("kind"), StringComparison.Ordinal)).Select(k => k.Label).FirstOrDefault() ?? "Date";
            var pretty = ContactJson.Get(a, "date") is { } d ? ContactDates.Pretty(d, today) : new PrettyDate(string.Empty, null);
            var text = pretty.Text;
            var place = ContactJson.Get(a, "place").Str("full") ?? ContactJson.Get(a, "place").Str("fullAddress");
            if (place is not null)
            {
                text = text.Length == 0 ? place : $"{text}, {place}";
            }

            var soon = !string.Equals(a.Str("kind"), "death", StringComparison.Ordinal) && pretty.DaysUntilNext is <= 14;
            v.Add(new ContactLine("DATES", kind, text) { Tint = soon ? "rose" : string.Empty });
        }

        foreach (var (name, r) in ContactJson.Entries(card, "relatedTo"))
        {
            var rel = ContactJson.FirstTrueKey(ContactJson.Get(r, "relation")) ?? string.Empty;
            var label = ContactOptions.Relations.Where(k => string.Equals(k.Key, rel, StringComparison.Ordinal)).Select(k => k.Label).FirstOrDefault() ?? (rel.Length == 0 ? "Related" : rel);
            v.Add(new ContactLine("RELATED PEOPLE", label, name.StartsWith("urn:uuid:", StringComparison.Ordinal) ? "(contact)" : name));
        }

        var nicks = ContactJson.Values(card, "nicknames").Select(n => n.Str("name")).OfType<string>().ToList();
        if (nicks.Count > 0)
        {
            v.Add(new ContactLine("ABOUT", "Nickname", string.Join(", ", nicks)));
        }

        foreach (var l in ContactJson.Values(card, "preferredLanguages"))
        {
            v.Add(new ContactLine("ABOUT", "Language", l.Str("language") ?? string.Empty));
        }

        foreach (var n in ContactJson.Values(card, "notes"))
        {
            if (n.Str("note") is { } t && t.Trim().Length > 0)
            {
                v.Add(new ContactLine("NOTES", "Note", t));
            }
        }

        return v;
    }

    /// <summary>The line under the name: job title, role and organisation with its units ("Countess · Analytical Engines, R&amp;D").</summary>
    public static string Subtitle(JsonNode card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var titles = ContactJson.Values(card, "titles");
        var title = titles.Where(t => string.Equals(t.Str("kind") ?? "title", "title", StringComparison.Ordinal)).Select(t => t.Str("name")).FirstOrDefault() ?? string.Empty;
        var role = titles.Where(t => string.Equals(t.Str("kind"), "role", StringComparison.Ordinal)).Select(t => t.Str("name")).FirstOrDefault() ?? string.Empty;
        var org = string.Empty;
        if (ContactJson.Values(card, "organizations").FirstOrDefault() is { } o)
        {
            var units = ContactJson.Get(o, "units") is JsonArray a ? a.Select(x => x.Str("name")).OfType<string>() : [];
            org = string.Join(", ", new[] { o.Str("name") ?? string.Empty }.Concat(units).Where(x => x.Length > 0));
        }

        return string.Join(" · ", new[] { title, role, org }.Where(x => x.Length > 0));
    }
}
