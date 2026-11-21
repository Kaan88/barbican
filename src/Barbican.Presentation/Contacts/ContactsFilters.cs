// Mirrors: Bulwark components/contacts/contact-list.tsx:34-90 (ListFilters, cycleTri, countActiveFilters, matchTri, getAnniversaryMonth) and :164-243 (the filter predicate: organization, job title, location, email domain, birthday month, has email / phone / photo). B-CON-004.
// The pure filter of the contact list. A tri-state is null (any), true (must have) or false (must not have). Cards are JSContact (RFC 9553) JSON.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.Presentation.Contacts;

/// <summary>The filters of the contact list panel (Bulwark's filter button beside the search field).</summary>
public sealed record ContactsFilters
{
    /// <summary>No filter.</summary>
    public static ContactsFilters None { get; } = new();

    public string Organization { get; init; } = string.Empty;

    public string JobTitle { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;

    public string EmailDomain { get; init; } = string.Empty;

    /// <summary>1 to 12, or null for any month.</summary>
    public int? BirthdayMonth { get; init; }

    public bool? HasEmail { get; init; }

    public bool? HasPhone { get; init; }

    public bool? HasPhoto { get; init; }

    /// <summary>How many filters are set (the badge on the filter button).</summary>
    public int ActiveCount =>
        (Trim(Organization).Length > 0 ? 1 : 0) + (Trim(JobTitle).Length > 0 ? 1 : 0) + (Trim(Location).Length > 0 ? 1 : 0) + (Trim(EmailDomain).Length > 0 ? 1 : 0)
        + (BirthdayMonth is not null ? 1 : 0) + (HasEmail is not null ? 1 : 0) + (HasPhone is not null ? 1 : 0) + (HasPhoto is not null ? 1 : 0);

    /// <summary>The next state of a tri-state chip: any, yes, no, any.</summary>
    public static bool? Cycle(bool? value) => value is null ? true : value is true ? false : null;

    /// <summary>Whether a card passes every set filter.</summary>
    public bool Matches(JsonNode card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (ActiveCount == 0)
        {
            return true;
        }

        var emails = ContactsJson.Emails(card).ToList();
        if (!Tri(emails.Count > 0, HasEmail) || !Tri(ContactsJson.All(card["phones"], "number").Any(), HasPhone) || !Tri(ContactsJson.PhotoUri(card) is not null || HasPhotoMedia(card), HasPhoto))
        {
            return false;
        }

        return Contains(ContactsJson.All(card["organizations"], "name").Concat(Units(card)), Organization)
            && Contains(ContactsJson.All(card["titles"], "name"), JobTitle)
            && Contains(AddressParts(card), Location)
            && DomainMatches(emails)
            && MonthMatches(card);
    }

    private static string Trim(string text) => (text ?? string.Empty).Trim();

    private static bool Tri(bool actual, bool? filter) => filter is null || actual == filter;

    private static bool Contains(IEnumerable<string> values, string needle)
    {
        var n = Trim(needle);
        return n.Length == 0 || values.Any(v => v.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    private bool DomainMatches(List<string> emails)
    {
        var domain = Trim(EmailDomain).TrimStart('@');
        if (domain.Length == 0)
        {
            return true;
        }

        return emails.Any(e => e.IndexOf('@', StringComparison.Ordinal) is var at and >= 0 && e[(at + 1)..].Contains(domain, StringComparison.OrdinalIgnoreCase));
    }

    private bool MonthMatches(JsonNode card)
    {
        if (BirthdayMonth is not { } month)
        {
            return true;
        }

        return card["anniversaries"] is JsonObject map && map.Any(p => string.Equals(ContactsJson.Str(p.Value, "kind"), "birth", StringComparison.Ordinal) && MonthOf(p.Value?["date"]) == month);
    }

    // a date is "YYYY-MM-DD", "--MM-DD", a partial date {month, day} or a timestamp {utc} (Bulwark getAnniversaryMonth)
    private static int? MonthOf(JsonNode? date)
    {
        if (date is JsonValue v && v.TryGetValue<string>(out var s))
        {
            if (s.StartsWith("--", StringComparison.Ordinal))
            {
                return s.Length >= 4 && int.TryParse(s.AsSpan(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var partial) ? partial : null;
            }

            return s.Length >= 7 && s[4] == '-' && int.TryParse(s.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var iso) ? iso : null;
        }

        if (date is JsonObject o)
        {
            if (o["month"] is JsonValue m && m.TryGetValue<int>(out var month) && month > 0)
            {
                return month;
            }

            if (DateTimeOffset.TryParse(ContactsJson.Str(o, "utc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var utc))
            {
                return utc.UtcDateTime.Month;
            }
        }

        return null;
    }

    private static IEnumerable<string> Units(JsonNode card)
    {
        if (card["organizations"] is not JsonObject map)
        {
            yield break;
        }

        foreach (var (_, org) in map)
        {
            if (org?["units"] is JsonArray units)
            {
                foreach (var unit in units)
                {
                    if (ContactsJson.Str(unit, "name") is { Length: > 0 } name)
                    {
                        yield return name;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> AddressParts(JsonNode card)
    {
        if (card["addresses"] is not JsonObject map)
        {
            yield break;
        }

        foreach (var (_, address) in map)
        {
            foreach (var key in new[] { "full", "fullAddress", "locality", "region", "country", "postcode", "street" })
            {
                if (ContactsJson.Str(address, key) is { Length: > 0 } part)
                {
                    yield return part;
                }
            }

            if (address?["components"] is JsonArray components)
            {
                foreach (var c in components)
                {
                    if (ContactsJson.Str(c, "value") is { Length: > 0 } value)
                    {
                        yield return value;
                    }
                }
            }
        }
    }

    // a photo held as a blob has no data: URI until it is fetched, but it is a photo
    private static bool HasPhotoMedia(JsonNode card) =>
        card["media"] is JsonObject media && media.Any(m => string.Equals(ContactsJson.Str(m.Value, "kind"), "photo", StringComparison.Ordinal));
}
