// Mirrors: Bulwark lib/global-search/providers/contacts.ts:10-68 (contactFields, toHit: the name, then the book and the address), providers/calendar.ts:8-52 (eventFields, one hit per series, after:/before: on the start day),
// providers/files.ts:96-125 (filterNodes: no trash, the word folder for directories) and lib/global-search/query-parser.ts matchesTerms. The stores already hold every card, event and node of the login (ContactStore, CalendarStore,
// FileStore), so these filters run over what they hold instead of asking the server again.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Files;

namespace Barbican.Core.Search;

/// <summary>The contacts, calendar and files hits of a parsed search over the loaded stores.</summary>
public static class GlobalLocalSearch
{
    /// <summary>The display name of a card: the full name, else the name parts, else the first address, else the nickname or organization.</summary>
    public static string ContactName(JsonNode card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card["name"]?.Str("full") is { Length: > 0 } full)
        {
            return full.Trim();
        }

        var parts = card["name"]?["components"] is JsonArray c ? string.Join(' ', c.OfType<JsonObject>().Select(p => p.Str("value")).Where(v => !string.IsNullOrWhiteSpace(v))) : string.Empty;
        if (parts.Length > 0)
        {
            return parts;
        }

        return Values(card, "emails", "address").Concat(Values(card, "nicknames", "name")).Concat(Values(card, "organizations", "name")).FirstOrDefault() ?? string.Empty;
    }

    /// <summary>Contact hits: every term in the name, an address, a phone, an organization or a nickname; groups are left out; the book and the first address are the second line.</summary>
    public static IReadOnlyList<SearchHit> Contacts(IReadOnlyList<JsonNode> cards, IReadOnlyList<JsonNode> books, ParsedSearch parsed)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(books);
        ArgumentNullException.ThrowIfNull(parsed);
        var hits = new List<SearchHit>();
        foreach (var card in cards)
        {
            if (string.Equals(card.Str("kind"), "group", StringComparison.Ordinal))
            {
                continue;
            }

            var name = ContactName(card);
            var emails = Values(card, "emails", "address").ToList();
            var phones = Values(card, "phones", "number").ToList();
            var orgs = Values(card, "organizations", "name").ToList();
            var fields = new List<string> { name };
            fields.AddRange(emails);
            fields.AddRange(orgs);
            fields.AddRange(phones);
            fields.AddRange(Values(card, "nicknames", "name"));
            if (!GlobalSearch.MatchesTerms(parsed.Terms, fields))
            {
                continue;
            }

            var book = BookName(card, books);
            var shown = emails.FirstOrDefault() is { Length: > 0 } e && !string.Equals(e, name, StringComparison.Ordinal) ? e : string.Empty;
            var details = new List<SearchDetail>();
            details.AddRange(emails.Select(x => new SearchDetail("mail", x)));
            details.AddRange(phones.Select(x => new SearchDetail("phone", x)));
            details.AddRange(orgs.Select(x => new SearchDetail("building", x)));
            hits.Add(new SearchHit(
                SearchScope.Contacts,
                card.Str("id") ?? string.Empty,
                name.Length > 0 ? name : card.Str("id") ?? string.Empty,
                string.Join(" · ", new[] { book, shown }.Where(x => x.Length > 0)),
                card.Str("updated") ?? string.Empty,
                card.Str("id") ?? string.Empty)
            {
                Details = details,
            });
        }

        return hits;
    }

    /// <summary>Event hits: every term in the title, description, a place or a guest; one hit per series (the master), after:/before: compared with the start day; tasks are left out; the calendar and the first place are the second line. The hit's place is the start day (yyyy-MM-dd).</summary>
    public static IReadOnlyList<SearchHit> Events(IReadOnlyList<JsonNode> events, IReadOnlyList<JsonNode> calendars, ParsedSearch parsed)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(calendars);
        ArgumentNullException.ThrowIfNull(parsed);
        var hits = new List<SearchHit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ev in events)
        {
            if (CalendarOccurrences.IsTask(ev))
            {
                continue;
            }

            var title = ev.Str("title") ?? string.Empty;
            var places = Values(ev, "locations", "name").ToList();
            var guests = Guests(ev);
            var fields = new List<string> { title, ev.Str("description") ?? string.Empty };
            fields.AddRange(places);
            fields.AddRange(guests);
            if (!GlobalSearch.MatchesTerms(parsed.Terms, fields))
            {
                continue;
            }

            var start = ev.Str("start") ?? string.Empty;
            var day = start.Length >= 10 ? start[..10] : string.Empty;
            if (day.Length > 0 && ((parsed.Mail.After is { Length: > 0 } after && string.CompareOrdinal(day, after) < 0)
                || (parsed.Mail.Before is { Length: > 0 } before && string.CompareOrdinal(day, before) > 0)))
            {
                continue;
            }

            if (ev.Str("uid") is { Length: > 0 } uid && !seen.Add(uid))
            {
                continue;
            }

            hits.Add(EventHit(ev, title, places, guests, start, day, CalendarName(ev, calendars)));
        }

        return hits;
    }

    private static SearchHit EventHit(JsonNode ev, string title, List<string> places, List<string> guests, string start, string day, string calendar)
    {
        var details = new List<SearchDetail>();
        if (start.Length > 0)
        {
            details.Add(new SearchDetail("clock", When(ev, start)));
        }

        details.AddRange(places.Select(x => new SearchDetail("pin", x)));
        if (ev.Str("description") is { Length: > 0 } text)
        {
            details.Add(new SearchDetail("text", text.Trim()));
        }

        if (guests.Count > 0)
        {
            details.Add(new SearchDetail("people", string.Join(", ", guests)));
        }

        return new SearchHit(
            SearchScope.Calendar,
            ev.Str("id") ?? string.Empty,
            title,
            string.Join(" · ", new[] { calendar, places.FirstOrDefault() ?? string.Empty }.Where(x => x.Length > 0)),
            start,
            day)
        {
            Details = details,
            IsRecurring = ev.Str("recurrenceId") is { Length: > 0 } || ev.Arr("recurrenceRules") is { Count: > 0 },
        };
    }

    /// <summary>File and folder hits: every term in the name or the kind (the word folder matches directories); nothing in the trash; the folder path is the second line, the entry's key the id, the folder to show the place.</summary>
    public static IReadOnlyList<SearchHit> Files(IReadOnlyList<FileEntry> entries, ParsedSearch parsed)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(parsed);
        var byKey = entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
        var trash = entries.FirstOrDefault(e => string.Equals(e.Name, GlobalSearch.FilesTrashName, StringComparison.Ordinal) && e.ParentKey is null && !e.Shared);
        var hits = new List<SearchHit>();
        foreach (var e in entries)
        {
            if (trash is not null && (string.Equals(e.Key, trash.Key, StringComparison.Ordinal) || UnderTrash(e, byKey, trash.Key)))
            {
                continue;
            }

            if (!GlobalSearch.MatchesTerms(parsed.Terms, [e.Name, e.IsDirectory ? "folder" : e.MediaType ?? string.Empty]))
            {
                continue;
            }

            var path = PathOf(e, byKey);
            var modified = e.Modified?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) ?? string.Empty;
            var details = new List<SearchDetail>();
            if (!e.IsDirectory)
            {
                details.Add(new SearchDetail("drive", string.Join(" · ", new[] { Size(e.Size), e.MediaType ?? string.Empty }.Where(x => x.Length > 0))));
            }

            if (modified.Length > 0)
            {
                details.Add(new SearchDetail("clock", FormatInstant(modified)));
            }

            details.Add(new SearchDetail("folder", path));
            hits.Add(new SearchHit(SearchScope.Files, e.Key, e.Name, path, modified, e.IsDirectory ? e.Key : e.ParentKey ?? string.Empty)
            {
                Details = details,
                IsFolder = e.IsDirectory,
            });
        }

        return hits;
    }

    /// <summary>An ISO instant as "yyyy-MM-dd HH:mm" (the preview's date lines); text that is not one stays as it is.</summary>
    public static string FormatInstant(string iso)
    {
        ArgumentNullException.ThrowIfNull(iso);
        return iso.Length >= 16 && iso[10] == 'T' ? iso[..10] + " " + iso[11..16] : iso;
    }

    /// <summary>A byte count as B, KB, MB or GB with one decimal from KB up (Bulwark lib/utils.ts formatFileSize).</summary>
    public static string Size(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        string[] units = ["KB", "MB", "GB", "TB"];
        var v = bytes / 1024d;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1)
        {
            v /= 1024;
            i++;
        }

        return v.ToString("0.#", CultureInfo.InvariantCulture) + " " + units[i];
    }

    private static IEnumerable<string> Values(JsonNode card, string map, string field) =>
        card[map] is JsonObject o ? o.Select(p => p.Value?.Str(field)).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!) : [];

    private static string BookName(JsonNode card, IReadOnlyList<JsonNode> books)
    {
        if (card["addressBookIds"] is not JsonObject ids)
        {
            return string.Empty;
        }

        foreach (var key in ids.Select(p => p.Key))
        {
            var book = books.FirstOrDefault(b => string.Equals(b.Str("id"), key, StringComparison.Ordinal));
            if (book?.Str("name") is { Length: > 0 } name)
            {
                return name;
            }
        }

        return string.Empty;
    }

    private static string CalendarName(JsonNode ev, IReadOnlyList<JsonNode> calendars)
    {
        var id = CalendarIds.FirstCalendar(ev);
        return calendars.FirstOrDefault(c => string.Equals(c.Str("id"), id, StringComparison.Ordinal))?.Str("name") ?? string.Empty;
    }

    private static List<string> Guests(JsonNode ev) =>
        ev["participants"] is JsonObject o
            ? [.. o.Select(p => p.Value?.Str("name") is { Length: > 0 } n ? n : p.Value?.Str("email") ?? string.Empty).Where(x => x.Length > 0)]
            : [];

    private static string When(JsonNode ev, string start) =>
        ev.IsTrue("showWithoutTime") || start.Length < 16
            ? start[..Math.Min(10, start.Length)] + " (all day)"
            : start[..10] + " " + start[11..16];

    private static bool UnderTrash(FileEntry e, Dictionary<string, FileEntry> byKey, string trashKey)
    {
        var cur = e.ParentKey;
        for (var guard = 0; cur is not null && guard <= 64; guard++)
        {
            if (string.Equals(cur, trashKey, StringComparison.Ordinal))
            {
                return true;
            }

            cur = byKey.TryGetValue(cur, out var parent) ? parent.ParentKey : null;
        }

        return false;
    }

    private static string PathOf(FileEntry e, Dictionary<string, FileEntry> byKey)
    {
        var segments = new List<string>();
        var cur = e.ParentKey;
        for (var guard = 0; cur is not null && guard <= 64 && byKey.TryGetValue(cur, out var parent); guard++)
        {
            segments.Insert(0, parent.Name);
            cur = parent.ParentKey;
        }

        return "/" + string.Join('/', segments);
    }
}
