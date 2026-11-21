// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:199-235 (ensureCalendars, pickCalendar), :329-392 (loadEvent, eventForMutation), lib/calendar-editability.ts:14-95 (canCreateEventsIn,
// eventHasNoOwner, getEventEditability: the CALENDAR's rights decide first, the organiser only refines mayWriteOwn) and assistant/c.calendar.ts:25-52 (own addresses). The rights read here are
// the calendars' myRights (JMAP Calendars, Stalwart crates/jmap/src/calendar/get.rs), never the mailbox rights an earlier review found being checked by mistake.
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;

namespace Barbican.Core.Plugins.Api.Calendar;

internal static class PluginCalendarData
{
    /// <summary>The login's calendar data; loaded first when it never was (ensureCalendars, :200-205). Null when the login has no calendars.</summary>
    internal static async Task<CalendarSnapshot?> SnapshotAsync(IPluginCalendarHost host, string accountKey, CancellationToken ct)
    {
        if (host.Store.Snapshot(accountKey) is { } known) return known;
        var loaded = await host.Store.LoadAsync(accountKey, ct).ConfigureAwait(false);
        return loaded.IsOk ? loaded.Value : null;
    }

    internal static JsonNode? CalendarById(CalendarSnapshot snapshot, string id) =>
        snapshot.Calendars.FirstOrDefault(c => string.Equals(c.Str("id"), id, StringComparison.Ordinal));

    /// <summary>The event or task a plugin named: the stored object by its id (an occurrence id names its series' object).</summary>
    internal static JsonNode? FindObject(CalendarSnapshot snapshot, string id)
    {
        var baseId = CalendarIds.BaseId(id);
        return snapshot.Events.FirstOrDefault(e => string.Equals(e.Str("id"), baseId, StringComparison.Ordinal));
    }

    /// <summary>The calendars a feed fills (read-only whatever their rights say).</summary>
    internal static async Task<HashSet<string>> FeedCalendarsAsync(IPluginCalendarHost host, string accountKey, CancellationToken ct)
    {
        if (host.Subscriptions is null) return [];
        try
        {
            return [.. (await host.Subscriptions.ListAsync(accountKey, ct).ConfigureAwait(false)).Select(s => s.CalendarId)];
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            return [];
        }
    }

    /// <summary>canCreateEventsIn (calendar-editability.ts:20-27): not a feed, and no rights or the right to write all or own.</summary>
    internal static bool CanCreateIn(JsonNode calendar, ISet<string> feeds)
    {
        if (feeds.Contains(calendar.Str("id") ?? "")) return false;
        return calendar.Child("myRights") is not JsonObject r || r.IsTrue("mayWriteAll") || r.IsTrue("mayWriteOwn");
    }

    /// <summary>
    /// getEventEditability (calendar-editability.ts:54-95) reduced to the yes or no a write needs: every calendar the object is in must allow it. mayWriteAll allows; mayWriteOwn allows the objects the user
    /// organises or that have no owner; rights not loaded allow only the user's own login's objects. A delete also needs mayDelete when the calendar states it.
    /// </summary>
    internal static async Task<bool> CanChangeAsync(IPluginCalendarHost host, IAccountSession session, CalendarSnapshot snapshot, JsonNode obj, bool delete, CancellationToken ct)
    {
        var feeds = await FeedCalendarsAsync(host, session.Account.Key, ct).ConfigureAwait(false);
        var ids = PluginCalendarProjection.TrueKeys(obj.Child("calendarIds"));
        if (ids.Any(feeds.Contains)) return false;
        var rights = ids.Select(id => CalendarById(snapshot, id)?.Child("myRights")).OfType<JsonObject>().ToList();
        if (rights.Count == 0) return obj.Str("_acc") is null && !(obj.Str("id") ?? "").Contains('~', StringComparison.Ordinal);
        if (delete && rights.Any(r => r.TryGetPropertyValue("mayDelete", out var may) && !(may is JsonValue v && v.TryGetValue<bool>(out var allowed) && allowed))) return false;
        if (rights.All(r => r.IsTrue("mayWriteAll"))) return true;
        if (!rights.All(r => r.IsTrue("mayWriteOwn"))) return false;
        var own = await OwnAsync(session, ct).ConfigureAwait(false);
        return !HasOwner(obj) || EventEditor.UserOrganizes(obj, own.Addresses);
    }

    /// <summary>The user's own addresses and the one they organise from (a login name that is an address, then the identities; c.calendar.ts:25-28).</summary>
    internal static async Task<(IReadOnlyList<string> Addresses, CalendarPerson? Organizer)> OwnAsync(IAccountSession session, CancellationToken ct)
    {
        var addresses = new List<string>();
        CalendarPerson? organizer = null;
        var user = session.Client.Session.Username;
        if (user.Contains('@', StringComparison.Ordinal)) addresses.Add(user);
        var identities = await session.Mail.GetIdentitiesAsync(ct).ConfigureAwait(false);
        if (identities.IsOk)
        {
            foreach (var identity in identities.Value!)
            {
                var email = identity.Email.Trim();
                if (!email.Contains('@', StringComparison.Ordinal)) continue;
                organizer ??= new CalendarPerson(identity.Name ?? "", email);
                if (!addresses.Contains(email, StringComparer.OrdinalIgnoreCase)) addresses.Add(email);
            }
        }

        organizer ??= addresses.Count > 0 ? new CalendarPerson("", addresses[0]) : null;
        return (addresses, organizer);
    }

    /// <summary>eventHasNoOwner, negated (calendar-editability.ts:30-40): an organiser address, a reply address or an owner participant.</summary>
    private static bool HasOwner(JsonNode obj)
    {
        if (obj.Str("organizerCalendarAddress") is not null) return true;
        if (obj.Child("replyTo") is JsonObject { Count: > 0 }) return true;
        return (obj.Obj("participants") ?? []).Any(p => p.Value.Child("roles").IsTrue("owner"));
    }

    /// <summary>pickCalendar (:218-235) for an event: the requested calendar when it exists and may be written, else the default one that shows events, else the first.</summary>
    internal static JsonNode? PickCalendar(CalendarSnapshot snapshot, string? requested)
    {
        if (requested is not null) return CalendarById(snapshot, requested);
        return snapshot.Calendars.FirstOrDefault(c => c.IsTrue("isDefault")) ?? (snapshot.Calendars.Count > 0 ? snapshot.Calendars[0] : null);
    }

    /// <summary>The id of the event the writer created, as the store holds it (a shared login's ids carry their namespace).</summary>
    internal static JsonNode? CreatedObject(CalendarSnapshot snapshot, string id) =>
        snapshot.Events.FirstOrDefault(e => string.Equals(e.Str("id"), id, StringComparison.Ordinal))
        ?? snapshot.Events.FirstOrDefault(e => string.Equals(e.Str("_rid"), id, StringComparison.Ordinal));
}
