// Mirrors: Rust reminders.rs:373 fetch (the events whose reminders can come due soon: Calendar/get for the default alerts, CalendarEvent/query between the last check and the window's end, get_all with 19 properties; every account with
// calendars, the user's own and the ones shared with them; a shared account that fails must not hide the user's own reminders). G-005: Rust's window ended at fetch_ahead (8 days or the longest calendar default alert), so an
// event's own alert longer than that never fired; the window here is at least OwnAlertHorizon (35 days) so an event 9 days out with a 9 day alert is read. Owner cal-data.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Requests;

namespace Barbican.Core.Calendar.Reminders;

public sealed partial class ReminderScheduler
{
    /// <summary>How far ahead events are read at least: five weeks, so an alert an event set for itself (weeks before) is found (G-005).</summary>
    public static readonly TimeSpan OwnAlertHorizon = TimeSpan.FromDays(35);

    private static readonly JsonArray CalendarProps = new("id", "name", "defaultAlertsWithTime", "defaultAlertsWithoutTime");

    private static readonly string[] EventProps =
    [
        "id", "@type", "uid", "title", "start", "duration", "timeZone", "showWithoutTime", "calendarIds", "recurrenceRules", "excludedRecurrenceRules", "recurrenceOverrides", "alerts", "useDefaultAlerts", "status", "due", "progress",
        "locations", "virtualLocations",
    ];

    /// <summary>Reads the events of one login; false (and the old events kept) when the primary account cannot be read.</summary>
    private async Task<bool> FetchAsync(Run run, long now, CancellationToken ct)
    {
        if (_accounts.SessionFor(run.Owner) is not { } session)
        {
            return false;
        }

        var client = session.Client;
        var primary = client.Session.AccountFor(JmapCapabilities.Calendars);
        var last = await _state.LastCheckAsync(run.Owner, ct).ConfigureAwait(false);
        var from = (last is { } l ? Math.Max(l, now - ReminderRules.MissedWindowMs) : now - ReminderRules.StaleMs) - 86_400_000;
        var cals = new List<JsonNode>();
        var events = new List<JsonNode>();
        foreach (var (account, _) in CalendarStore.CalendarAccounts(client.Session, primary))
        {
            var one = await FetchAccountAsync(client, account, primary, from, now, ct).ConfigureAwait(false);
            if (one is null)
            {
                // a shared account that fails must not hide the user's own reminders
                if (string.Equals(account, primary, StringComparison.Ordinal))
                {
                    return false;
                }

                continue;
            }

            cals.AddRange(one.Value.Calendars);
            events.AddRange(one.Value.Events);
        }

        lock (_lock)
        {
            run.Calendars = cals;
            run.Events = events;
            run.FetchedMs = now;
            run.Dirty = false;
        }

        return true;
    }

    private static async Task<(List<JsonNode> Calendars, List<JsonNode> Events)?> FetchAccountAsync(IJmapClient client, string account, string primary, long fromMs, long nowMs, CancellationToken ct)
    {
        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("Calendar/get", new JsonObject { ["accountId"] = account, ["ids"] = null, ["properties"] = CalendarProps.DeepClone() }, "c")), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return null;
        }

        var cals = (r.Value!.MethodResponses[0][1].Arr("list") ?? []).OfType<JsonNode>().ToList();
        var ahead = Math.Max(ReminderRules.FetchAhead(cals), (long)OwnAlertHorizon.TotalMilliseconds);
        var filter = new JsonObject { ["after"] = Utc(fromMs), ["before"] = Utc(nowMs + ahead) };
        var ids = await client.QueryAllAsync("CalendarEvent/query", account, new JsonObject { ["filter"] = filter }, ct).ConfigureAwait(false);
        if (!ids.IsOk)
        {
            return null;
        }

        var got = await client.GetAllAsync("CalendarEvent/get", account, ids.Value!, new JsonArray([.. EventProps.Select(p => (JsonNode)p)]), ct).ConfigureAwait(false);
        if (!got.IsOk)
        {
            return null;
        }

        foreach (var c in cals.OfType<JsonObject>())
        {
            c["id"] = CalendarIds.Ns(primary, account, c.Str("id") ?? string.Empty);
        }

        List<JsonNode> events = [.. got.Value!];
        foreach (var e in events)
        {
            CalendarIds.NamespaceEvent(e, primary, account);
        }

        return (cals, events);
    }

    private static string Utc(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
