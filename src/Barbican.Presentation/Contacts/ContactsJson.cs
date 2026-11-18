// Mirrors: RFC 9553 JSContact Card (maps of entries keyed by an id), RFC 9610 ContactCard; Rust contacts.rs (card reads). Owner con-ui: the reads the list, the sidebar and the menus make on a card (con-data owns the writes and the form).

using System.Text.Json.Nodes;

namespace Barbican.Presentation.Contacts;

/// <summary>Small reads on JSContact cards shared by the contacts view model and menus.</summary>
public static class ContactsJson
{
    private const string UuidPrefix = "urn:uuid:";

    /// <summary>The <c>tel:</c> address of a phone number as people type it ("+44 20 7946-0002", "(020) 7946 0002"): digits and a leading plus only, 3 to 20 digits; null for anything else (a letter, a second plus, too short or too long), which then gets no Call link.</summary>
    public static Uri? TelUri(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return null;
        }

        var text = number.Trim();
        var plus = text.StartsWith('+');
        var digits = new System.Text.StringBuilder();
        foreach (var c in plus ? text[1..] : text)
        {
            if (char.IsAsciiDigit(c))
            {
                _ = digits.Append(c);
            }
            else if (c is not (' ' or '-' or '.' or '(' or ')' or '\u00a0'))
            {
                return null;
            }
        }

        return digits.Length is >= 3 and <= 20 ? new Uri("tel:" + (plus ? "+" : string.Empty) + digits, UriKind.Absolute) : null;
    }

    /// <summary>A string property, or an empty string.</summary>
    public static string Str(JsonNode? node, string key) => node?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;

    /// <summary>The server id of a card or book.</summary>
    public static string Id(JsonNode card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return Str(card, "id");
    }

    /// <summary>Whether the card is a contact group (kind "group").</summary>
    public static bool IsGroup(JsonNode card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return string.Equals(Str(card, "kind"), "group", StringComparison.Ordinal);
    }

    /// <summary>The values of <paramref name="field"/> across the entries of a map property, in map order, empty ones left out.</summary>
    public static IEnumerable<string> All(JsonNode? map, string field)
    {
        if (map is not JsonObject o)
        {
            yield break;
        }

        foreach (var (_, entry) in o)
        {
            if (Str(entry, field) is { Length: > 0 } v)
            {
                yield return v;
            }
        }
    }

    /// <summary>The first non-empty value of <paramref name="field"/> in a map property, or an empty string.</summary>
    public static string FirstOf(JsonNode? map, string field) => All(map, field).FirstOrDefault() ?? string.Empty;

    /// <summary>The email addresses of a card, trimmed.</summary>
    public static IEnumerable<string> Emails(JsonNode card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return All(card["emails"], "address").Select(a => a.Trim()).Where(a => a.Length > 0);
    }

    /// <summary>The keys of an object property (keywords, addressBookIds, members).</summary>
    public static IReadOnlyList<string> Keys(JsonNode? node, string property) => node?[property] is JsonObject o ? [.. o.Select(p => p.Key)] : [];

    /// <summary>The keys under which a card is a group member: its uid, the uid without or with "urn:uuid:", and its id (con-data's ContactMerge.MemberKeys does the same; this copy keeps the list pure).</summary>
    public static IReadOnlyList<string> MemberKeys(JsonNode card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var keys = new List<string>();
        var uid = Str(card, "uid");
        if (uid.Length > 0)
        {
            keys.Add(uid);
            keys.Add(uid.StartsWith(UuidPrefix, StringComparison.Ordinal) ? uid[UuidPrefix.Length..] : UuidPrefix + uid);
        }

        if (Id(card).Length > 0)
        {
            keys.Add(Id(card));
        }

        return keys;
    }

    /// <summary>Whether <paramref name="group"/> lists the card as a member.</summary>
    public static bool HasMember(JsonNode group, JsonNode card)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group["members"] is JsonObject members && MemberKeys(card).Any(members.ContainsKey);
    }

    /// <summary>The first photo of a card as a data: URI (a blob photo has none until it is fetched), or null.</summary>
    public static string? PhotoUri(JsonNode card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card["media"] is not JsonObject media)
        {
            return null;
        }

        foreach (var (_, m) in media)
        {
            var uri = Str(m, "uri");
            var kind = Str(m, "kind");
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && (kind.Length == 0 || string.Equals(kind, "photo", StringComparison.Ordinal)))
            {
                return uri;
            }
        }

        return null;
    }
}
