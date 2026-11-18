// Mirrors: Rust cal_editor.rs:69 initials, :735 parse_person, cal_extra.rs:53 people_matching, :84 birthday_parts is in BirthdayCalendar; Bulwark components/calendar/participant-input.tsx, lib/calendar-participants.ts.
// D-M4-06: the Rust code kept the contact cards in a thread-local; here they are an argument.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Editing;

/// <summary>Guests as text: parsing, initials and the contact search.</summary>
public static class CalendarPeople
{
    /// <summary>"Ann Lee &lt;ann@x.example&gt;" and a bare "bob@y.example" parse; "not an address" and a dotless host ("a@b") give null.</summary>
    public static CalendarPerson? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var t = text.Trim();
        var l = t.LastIndexOf('<');
        var r = t.LastIndexOf('>');
        if (l >= 0 && r > l)
        {
            var email = t[(l + 1)..r].Trim();
            return IsAddress(email) ? new CalendarPerson(t[..l].Trim().Trim('"'), email) : null;
        }

        return IsAddress(t) ? new CalendarPerson(string.Empty, t) : null;
    }

    internal static bool IsAddress(string text)
    {
        var s = text.Trim();
        var at = s.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || s.IndexOf('@', at + 1) >= 0 || s.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var domain = s[(at + 1)..];
        return domain.Contains('.', StringComparison.Ordinal) && !domain.StartsWith('.') && !domain.EndsWith('.');
    }

    /// <summary>"AL" for "Ann Lee"; from the address ("bob.smith@y.example" gives "BS") when there is no name.</summary>
    public static string Initials(string name, string email)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(email);
        var source = string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name;
        var parts = source.Split([' ', '\t', '\r', '\n', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        var s = parts.Length switch
        {
            0 => "?",
            1 => string.Concat(TextElements(parts[0]).Take(2)),
            _ => string.Concat(parts.Take(2).Select(p => TextElements(p).FirstOrDefault() ?? "")),
        };
        return s.ToUpperInvariant();
    }

    private static IEnumerable<string> TextElements(string text)
    {
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            yield return (string)e.Current;
        }
    }

    /// <summary>Contacts and groups (JSContact cards) whose name or address contains <paramref name="query"/>, ranked name first, without the addresses in <paramref name="have"/>; a group lists its member cards.</summary>
    public static IReadOnlyList<PersonMatch> Matching(IReadOnlyList<JsonNode> cards, string query, IReadOnlyList<string> have, int max)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(have);
        var q = query.Trim().ToLowerInvariant();
        var haveSet = have.Select(h => h.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var found = new List<PersonMatch>();
        foreach (var c in cards)
        {
            var name = CardName(c);
            if (string.Equals((string?)c["kind"], "group", StringComparison.Ordinal))
            {
                if (!name.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var uids = (c["members"] as JsonObject)?.Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal) ?? [];
                var members = cards.Where(m => (string?)m["uid"] is { } u && uids.Contains(u))
                    .Select(m => (Email: CardEmails(m) is { Count: > 0 } emails ? emails[0] : null, Name: CardName(m)))
                    .Where(m => m.Email is not null)
                    .Select(m => new CalendarPerson(m.Name, m.Email!))
                    .ToList();
                if (members.Count > 0)
                {
                    found.Add(new PersonMatch(new CalendarPerson(name, string.Create(CultureInfo.InvariantCulture, $"{members.Count} members")), members));
                }

                continue;
            }

            foreach (var email in CardEmails(c))
            {
                if (haveSet.Contains(email.ToLowerInvariant()))
                {
                    continue;
                }

                if (name.Contains(q, StringComparison.OrdinalIgnoreCase) || email.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(new PersonMatch(new CalendarPerson(name, email), []));
                }
            }

            if (found.Count >= max * 3)
            {
                break;
            }
        }

        return found
            .OrderBy(m => !(m.Person.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) || m.Person.Email.StartsWith(q, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(m => m.Person.Name.ToLowerInvariant(), StringComparer.Ordinal)
            .Take(max)
            .ToList();
    }

    /// <summary>The display name of a JSContact card: the full name, else its name components joined.</summary>
    internal static string CardName(JsonNode card)
    {
        if ((string?)card["name"]?["full"] is { Length: > 0 } full)
        {
            return full;
        }

        var parts = (card["name"]?["components"] as JsonArray)?.Select(x => (string?)x?["value"]).OfType<string>() ?? [];
        return string.Join(' ', parts);
    }

    internal static IReadOnlyList<string> CardEmails(JsonNode card) =>
        (card["emails"] as JsonObject)?.Select(kv => (string?)kv.Value?["address"]).OfType<string>().ToList() ?? [];
}
