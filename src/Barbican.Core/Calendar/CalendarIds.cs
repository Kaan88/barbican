// Mirrors: Rust calendar.rs:226 ns, :231 split_ns, :236 server_ref, :242 server_calendar_ids, :248 base_id, :251 rid_of, :341 namespace_event; B-CAL-001. Another account's ids are prefixed "<account>~" so ids from several
// accounts never collide. Contract from the M4 plan section 4.2; owner cal-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar;

/// <summary>Namespaced calendar and event ids.</summary>
public static class CalendarIds
{
    /// <summary>The primary account's ids stay plain; a shared account's are "x~id".</summary>
    public static string Ns(string primaryAccount, string account, string id) =>
        string.Equals(account, primaryAccount, StringComparison.Ordinal) || string.IsNullOrEmpty(account) ? id : $"{account}~{id}";

    public static (string? Account, string Id) SplitNs(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var i = id.IndexOf('~', StringComparison.Ordinal);
        return i < 0 ? (null, id) : (id[..i], id[(i + 1)..]);
    }

    /// <summary>"e|2026-01-01T00:00:00" is "e".</summary>
    public static string BaseId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var i = id.IndexOf('|', StringComparison.Ordinal);
        return i < 0 ? id : id[..i];
    }

    /// <summary>"e|2026-01-01T00:00:00" is "2026-01-01T00:00:00"; null for a plain id.</summary>
    public static string? RecurrenceIdOf(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var i = id.IndexOf('|', StringComparison.Ordinal);
        return i < 0 ? null : id[(i + 1)..];
    }

    /// <summary>Gives an event of another account namespaced ids (its own and its calendars'); <c>_rid</c> keeps the server id and <c>_acc</c> the account (Rust namespace_event).</summary>
    public static void NamespaceEvent(JsonNode ev, string primaryAccount, string account)
    {
        if (ev is not JsonObject e)
        {
            return;
        }

        var raw = e.Str("id") ?? string.Empty;
        e["_rid"] = raw;
        e["_acc"] = account;
        e["id"] = Ns(primaryAccount, account, raw);
        if (!string.Equals(account, primaryAccount, StringComparison.Ordinal) && e["calendarIds"] is JsonObject ids)
        {
            var m = new JsonObject();
            foreach (var (k, v) in ids)
            {
                m[Ns(primaryAccount, account, k)] = v?.DeepClone();
            }

            e["calendarIds"] = m;
        }
    }

    /// <summary>The calendarIds map with the namespace removed, for the server.</summary>
    public static JsonNode ServerCalendarIds(JsonNode calendarIds)
    {
        var m = new JsonObject();
        if (calendarIds is JsonObject o)
        {
            foreach (var (k, v) in o)
            {
                m[SplitNs(k).Id] = v?.DeepClone();
            }
        }

        return m;
    }

    /// <summary>The calendar an event is in: the first calendarIds entry that is true, else the first key (Rust first_calendar); empty when none.</summary>
    public static string FirstCalendar(JsonNode ev)
    {
        if (ev.Obj("calendarIds") is not { } o)
        {
            return string.Empty;
        }

        foreach (var (k, v) in o)
        {
            if (v is JsonValue jv && jv.TryGetValue<bool>(out var b) && b)
            {
                return k;
            }
        }

        return o.FirstOrDefault().Key ?? string.Empty;
    }
}
