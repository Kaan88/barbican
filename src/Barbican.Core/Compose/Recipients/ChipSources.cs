// Mirrors: Rust chips.rs:240 blur_commits, :332 sources_from_cards, :356 recent_from_sent; Bulwark stores/contact-store.ts (loadRecentRecipients), email-composer.tsx recipient field. Port by M3 composer-ui.

#pragma warning disable MA0006 // JSON strings compare ordinally by nature

using System.Text.Json.Nodes;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Compose.Recipients;

/// <summary>The data side of the recipient chips.</summary>
public static class ChipSources
{
    /// <summary>Leaving a field: blank text never commits; with no suggestion list open, or when the text holds a comma or semicolon, it commits; otherwise (suggestions open) only when it already is a valid address, so partial text such as "sa" stays in the input.</summary>
    public static bool BlurCommits(string input, bool suggestionsOpen)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Trim().Length == 0)
        {
            return false;
        }

        if (!suggestionsOpen || input.IndexOfAny([',', ';']) >= 0)
        {
            return true;
        }

        return RecipientText.IsValidEmail(RecipientText.ParseRecipient(input).Email);
    }

    /// <summary>JSContact cards as suggestion sources: a person per card address (preferred address first; the name is <c>name.full</c> or the given and surname components), and a group per <c>kind: group</c> card with the members (by uid with or without <c>urn:uuid:</c>, or id) that have an address and a true flag.</summary>
    public static (IReadOnlyList<(string Name, string Email)> People, IReadOnlyList<(string Id, string Name, IReadOnlyList<Recipient> Members)> Groups) SourcesFromCards(IReadOnlyList<JsonObject> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var people = new List<(string, string)>();
        var persons = cards.Where(c => Str(c["kind"]) != "group").ToList();
        foreach (var c in persons)
        {
            var n = CardName(c);
            foreach (var e in CardEmails(c))
            {
                people.Add((n, e));
            }
        }

        var groups = new List<(string, string, IReadOnlyList<Recipient>)>();
        foreach (var g in cards.Where(c => Str(c["kind"]) == "group"))
        {
            var keys = g["members"] is JsonObject m
                ? m.Where(kv => kv.Value is JsonValue v && v.TryGetValue<bool>(out var b) && b).Select(kv => Bare(kv.Key)).ToList()
                : [];
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var members = new List<Recipient>();
            foreach (var c in persons)
            {
                var id = Str(c["id"]);
                var uid = Bare(Str(c["uid"]));
                if (!keys.Any(k => k == id || (uid.Length > 0 && k == uid)))
                {
                    continue;
                }

                if (CardEmails(c).FirstOrDefault() is { } e && seen.Add(e))
                {
                    var n = CardName(c);
                    members.Add(Recipient.Addr(n == e ? "" : n, e));
                }
            }

            groups.Add((Str(g["id"]), CardName(g), members));
        }

        return (people, groups);
    }

    /// <summary>The people written to in the given sent messages (newest first; To, then Cc, of each): one entry per address (case-insensitive; the first name and spelling win), blank addresses skipped (Bulwark loadRecentRecipients).</summary>
    public static IReadOnlyList<(string Name, string Email)> RecentFromSent(IReadOnlyList<EmailSummary> sent)
    {
        ArgumentNullException.ThrowIfNull(sent);
        var output = new List<(string Name, string Email)>();
        foreach (var m in sent)
        {
            foreach (var x in m.To.Concat(m.Cc))
            {
                var e = x.Email?.Trim() ?? "";
                if (e.Length == 0 || output.Any(o => string.Equals(o.Email, e, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                output.Add(((x.Name ?? "").Trim(), e));
            }
        }

        return output;
    }

    private static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    private static string Bare(string k) => k.StartsWith("urn:uuid:", StringComparison.Ordinal) ? k["urn:uuid:".Length..] : k;

    private static string CardName(JsonObject c)
    {
        var name = c["name"] as JsonObject;
        if (Str(name?["full"]).Trim() is { Length: > 0 } full)
        {
            return full;
        }

        var parts = (name?["components"] as JsonArray)?.OfType<JsonObject>().Where(x => Str(x["kind"]) != "separator").Select(x => Str(x["value"])).Where(s => s.Length > 0) ?? [];
        return string.Join(' ', parts).Trim();
    }

    private static List<string> CardEmails(JsonObject c)
    {
        if (c["emails"] is not JsonObject o)
        {
            return [];
        }

        return [.. o.Select(kv => kv.Value as JsonObject)
            .Where(e => e is not null && Str(e["address"]).Trim().Length > 0)
            .Select(e => (Pref: e!["pref"] is JsonValue p && p.TryGetValue<long>(out var l) ? l : 100, Addr: Str(e["address"]).Trim()))
            .OrderBy(x => x.Pref)
            .Select(x => x.Addr)];
    }
}
