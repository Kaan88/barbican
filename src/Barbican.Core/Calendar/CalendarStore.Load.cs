// Mirrors: Rust calendar.rs:273 load and :254 calendar_accounts (the primary account, then the accounts shared with the login that have the Calendars capability), :299 shared calendars' _shared/_owner/_color. Owner cal-data.

using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Calendar;

public sealed partial class CalendarStore
{
    /// <inheritdoc/>
    public async Task<Result<CalendarSnapshot>> LoadAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        if (_accounts.SessionFor(accountKey) is not { } session)
        {
            return Result<CalendarSnapshot>.Fail(new ValidationError("This account is not signed in."));
        }

        if (!session.Client.Session.Has(JmapCapabilities.Calendars))
        {
            lock (_lock)
            {
                _ = _held.Remove(accountKey);
            }

            return Result<CalendarSnapshot>.Ok(new CalendarSnapshot(accountKey, [], [], null, null));
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LoadLockedAsync(accountKey, session.Client, ct).ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private async Task<Result<CalendarSnapshot>> LoadLockedAsync(string key, IJmapClient client, CancellationToken ct)
    {
        var js = client.Session;
        var primary = js.AccountFor(JmapCapabilities.Calendars);
        var range = await _hooks.ShapeAsync(HookNames.OnBeforeFetchEvents, new FetchEventsRangePayload(null, null), ct).ConfigureAwait(false);
        var held = new Held(primary);
        IReadOnlyList<JsonNode> events = [];
        foreach (var (account, name) in CalendarAccounts(js, primary))
        {
            var one = await LoadAccountAsync(client, account, primary, name, range, ct).ConfigureAwait(false);
            if (!one.IsOk)
            {
                // a shared account that fails must not hide the user's own calendar
                if (string.Equals(account, primary, StringComparison.Ordinal))
                {
                    return Result<CalendarSnapshot>.Fail(one.Error!);
                }

                continue;
            }

            held.Calendars.AddRange(one.Value!.Calendars);
            events = [.. events, .. one.Value.Events];
            held.States[account] = (one.Value.CalendarState, one.Value.EventState);
        }

        events = await _hooks.ShapeAsync(HookNames.OnFetchingEvents, events, ct).ConfigureAwait(false);
        held.SetEvents(key, [.. events]);
        lock (_lock)
        {
            _held[key] = held;
        }

        var tasks = events.Where(CalendarOccurrences.IsTask).ToList();
        _hooks.Fire(HookNames.OnTasksLoaded, () => tasks);
        Raise(new CalendarChangedEventArgs(key, true, []));
        return Result<CalendarSnapshot>.Ok(held.Snapshot);
    }

    /// <summary>The calendars and events of one server account: Calendar/get, then every event id (paged) and the events in maxObjectsInGet chunks, never one back-referenced get.</summary>
    private async Task<Result<AccountLoad>> LoadAccountAsync(IJmapClient client, string account, string primary, string owner, FetchEventsRangePayload range, CancellationToken ct)
    {
        var cals = await CalendarsAsync(client, account, primary, owner, ct).ConfigureAwait(false);
        if (!cals.IsOk)
        {
            return Result<AccountLoad>.Fail(cals.Error!);
        }

        // the baseline for /changes is read before the events, so a change made while they load is not missed
        var baseline = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("CalendarEvent/get", new JsonObject { ["accountId"] = account, ["ids"] = new JsonArray(), ["properties"] = new JsonArray("id") }, "s")), ct).ConfigureAwait(false);
        if (!baseline.IsOk)
        {
            return Result<AccountLoad>.Fail(baseline.Error!);
        }

        var state = baseline.Value!.MethodResponses[0][1].Str("state");
        var query = new JsonObject();
        var filter = new JsonObject();
        if (range.Start is { } after)
        {
            filter["after"] = after.EndsWith('Z') ? after : after + "Z";
        }

        if (range.End is { } before)
        {
            filter["before"] = before.EndsWith('Z') ? before : before + "Z";
        }

        if (filter.Count > 0)
        {
            query["filter"] = filter;
        }

        var ids = await client.QueryAllAsync("CalendarEvent/query", account, query, ct).ConfigureAwait(false);
        if (!ids.IsOk)
        {
            return Result<AccountLoad>.Fail(ids.Error!);
        }

        var got = await client.GetAllAsync("CalendarEvent/get", account, ids.Value!, new JsonArray([.. EventProps.Select(p => (JsonNode)p)]), ct).ConfigureAwait(false);
        if (!got.IsOk)
        {
            return Result<AccountLoad>.Fail(got.Error!);
        }

        List<JsonNode> events = [.. got.Value!];
        foreach (var e in events)
        {
            CalendarIds.NamespaceEvent(e, primary, account);
        }

        return Result<AccountLoad>.Ok(new AccountLoad(cals.Value!.Calendars, events, cals.Value.CalendarState, state));
    }

    private async Task<Result<AccountLoad>> CalendarsAsync(IJmapClient client, string account, string primary, string owner, CancellationToken ct)
    {
        var args = new JsonObject { ["accountId"] = account, ["ids"] = null, ["properties"] = new JsonArray([.. CalendarProps.Select(p => (JsonNode)p)]) };
        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("Calendar/get", args, "c")), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<AccountLoad>.Fail(r.Error!);
        }

        var res = r.Value!.MethodResponses[0][1];
        var list = (res.Arr("list") ?? []).OfType<JsonObject>().OrderBy(c => c.Long("sortOrder") ?? 0).ThenBy(c => (c.Str("name") ?? string.Empty).ToLowerInvariant(), StringComparer.Ordinal).ToList();
        var colors = _prefs?.Current.Values["sharedCalendarColors"] as JsonObject;
        var shared = !string.Equals(account, primary, StringComparison.Ordinal);
        foreach (var cal in list)
        {
            var raw = cal.Str("id") ?? string.Empty;
            cal["_rid"] = raw;
            cal["_acc"] = account;
            cal["id"] = CalendarIds.Ns(primary, account, raw);
            if (shared)
            {
                cal["_shared"] = true;
                cal["_owner"] = owner;
                if (colors.Str($"{client.Session.Username}|{account}|{raw}") is { } color)
                {
                    cal["_color"] = color;
                }
            }
        }

        return Result<AccountLoad>.Ok(new AccountLoad([.. list], [], res.Str("state"), null));
    }

    /// <summary>The primary calendar account, then the accounts shared with the login that have the Calendars capability (Rust calendar_accounts).</summary>
    internal static List<(string Account, string Name)> CalendarAccounts(JmapSession session, string primary)
    {
        var output = new List<(string, string)> { (primary, NameOf(session, primary)) };
        if (session.Raw.ValueKind == JsonValueKind.Object && session.Raw.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Object)
        {
            foreach (var a in accounts.EnumerateObject())
            {
                if (!string.Equals(a.Name, primary, StringComparison.Ordinal) && a.Value.TryGetProperty("accountCapabilities", out var caps) && caps.ValueKind == JsonValueKind.Object && caps.TryGetProperty(JmapCapabilities.Calendars, out _))
                {
                    output.Add((a.Name, NameOf(session, a.Name)));
                }
            }
        }

        return output;
    }

    private static string NameOf(JmapSession session, string account) =>
        session.Raw.ValueKind == JsonValueKind.Object && session.Raw.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Object
        && accounts.TryGetProperty(account, out var a) && a.ValueKind == JsonValueKind.Object && a.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? account : account;
}
