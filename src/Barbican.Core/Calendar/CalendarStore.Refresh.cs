// Mirrors: RFC 8620 5.2 (Foo/changes from a state, hasMoreChanges, cannotCalculateChanges), Rust mail.rs:254 load_mailboxes (a delta from the kept state, else a full read; the same rule for calendars), PORT-PLAN section 3.4, D-M4-12;
// Stalwart crates/jmap/src/calendar/changes.rs and calendar_event/changes.rs. Owner cal-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Requests;

namespace Barbican.Core.Calendar;

public sealed partial class CalendarStore
{
    /// <inheritdoc/>
    public async Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        Held? held;
        lock (_lock)
        {
            _ = _held.TryGetValue(accountKey, out held);
        }

        if (held is null || _accounts.SessionFor(accountKey) is not { } session)
        {
            var loaded = await LoadAsync(accountKey, ct).ConfigureAwait(false);
            return loaded.IsOk ? Result<Unit>.Ok(default) : Result<Unit>.Fail(loaded.Error!);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await ApplyChangesAsync(accountKey, held, session.Client, ct).ConfigureAwait(false);

            // the server cannot say what changed from this state: read everything again (a fresh Held replaces this one)
            if (!result.IsOk && result.Error is MethodError { Type: "cannotCalculateChanges" })
            {
                var loaded = await LoadLockedAsync(accountKey, session.Client, ct).ConfigureAwait(false);
                return loaded.IsOk ? Result<Unit>.Ok(default) : Result<Unit>.Fail(loaded.Error!);
            }

            return result;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private async Task<Result<Unit>> ApplyChangesAsync(string key, Held held, IJmapClient client, CancellationToken ct)
    {
        var touched = new List<string>();
        var calendarsChanged = false;
        foreach (var account in held.States.Keys.ToList())
        {
            var one = await ApplyAccountAsync(key, held, client, account, ct).ConfigureAwait(false);
            if (!one.IsOk)
            {
                return Result<Unit>.Fail(one.Error!);
            }

            touched.AddRange(one.Value!.Touched);
            calendarsChanged |= one.Value.Calendars;
        }

        if (touched.Count > 0 || calendarsChanged)
        {
            Raise(new CalendarChangedEventArgs(key, calendarsChanged, touched));
        }

        return Result<Unit>.Ok(default);
    }

    private async Task<Result<Applied>> ApplyAccountAsync(string key, Held held, IJmapClient client, string account, CancellationToken ct)
    {
        var (calState, eventState) = held.States[account];
        if (calState is null || eventState is null)
        {
            return Result<Applied>.Fail(new MethodError("Calendar/changes", "cannotCalculateChanges", "no state kept"));
        }

        var cals = await ChangesAsync(client, "Calendar", account, calState, ct).ConfigureAwait(false);
        if (!cals.IsOk)
        {
            return Result<Applied>.Fail(cals.Error!);
        }

        var evs = await ChangesAsync(client, "CalendarEvent", account, eventState, ct).ConfigureAwait(false);
        if (!evs.IsOk)
        {
            return Result<Applied>.Fail(evs.Error!);
        }

        var calendarsMoved = cals.Value!.Changed.Count + cals.Value.Destroyed.Count > 0;
        List<JsonNode>? calendars = null;
        if (calendarsMoved)
        {
            var again = await CalendarsAsync(client, account, held.Primary, NameOf(client.Session, account), ct).ConfigureAwait(false);
            if (!again.IsOk)
            {
                return Result<Applied>.Fail(again.Error!);
            }

            calendars = again.Value!.Calendars;
        }

        var fetched = await FetchChangedAsync(client, account, held.Primary, evs.Value!.Changed, ct).ConfigureAwait(false);
        if (!fetched.IsOk)
        {
            return Result<Applied>.Fail(fetched.Error!);
        }

        var gone = evs.Value.Destroyed.Select(i => CalendarIds.Ns(held.Primary, account, i)).ToList();
        Merge(key, held, account, calendars, gone, fetched.Value!, (cals.Value.NewState, evs.Value.NewState));
        return Result<Applied>.Ok(new Applied([.. gone, .. fetched.Value!.Select(e => e.Str("id") ?? string.Empty)], calendarsMoved));
    }

    /// <summary>Puts what /changes brought into the held data of one account: new calendar list, removed and replaced or added events, the new states.</summary>
    private void Merge(string key, Held held, string account, List<JsonNode>? calendars, List<string> gone, List<JsonNode> fetched, (string? Calendar, string? Event) states)
    {
        lock (_lock)
        {
            if (calendars is not null)
            {
                held.Calendars = [.. held.Calendars.Where(c => !string.Equals(c.Str("_acc"), account, StringComparison.Ordinal)), .. calendars];
            }

            var events = held.Events.Where(e => !gone.Contains(e.Str("id") ?? string.Empty, StringComparer.Ordinal)).ToList();
            foreach (var e in fetched)
            {
                var at = events.FindIndex(x => string.Equals(x.Str("id"), e.Str("id"), StringComparison.Ordinal));
                if (at >= 0)
                {
                    events[at] = e;
                }
                else
                {
                    events.Add(e);
                }
            }

            held.States[account] = states;
            held.SetEvents(key, events);
        }
    }

    private static async Task<Result<List<JsonNode>>> FetchChangedAsync(IJmapClient client, string account, string primary, List<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return Result<List<JsonNode>>.Ok([]);
        }

        var got = await client.GetAllAsync("CalendarEvent/get", account, ids, new JsonArray([.. EventProps.Select(p => (JsonNode)p)]), ct).ConfigureAwait(false);
        if (!got.IsOk)
        {
            return Result<List<JsonNode>>.Fail(got.Error!);
        }

        List<JsonNode> events = [.. got.Value!];
        foreach (var e in events)
        {
            CalendarIds.NamespaceEvent(e, primary, account);
        }

        return Result<List<JsonNode>>.Ok(events);
    }

    /// <summary>The created, updated and destroyed ids since a state, all pages of it (hasMoreChanges).</summary>
    private static async Task<Result<ChangeSet>> ChangesAsync(IJmapClient client, string type, string account, string since, CancellationToken ct)
    {
        var changed = new List<string>();
        var destroyed = new List<string>();
        var state = since;
        while (true)
        {
            var args = new JsonObject { ["accountId"] = account, ["sinceState"] = state };
            var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call(type + "/changes", args, "h")), ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<ChangeSet>.Fail(r.Error!);
            }

            var res = r.Value!.MethodResponses[0][1];
            changed.AddRange(Ids(res.Arr("created")));
            changed.AddRange(Ids(res.Arr("updated")));
            destroyed.AddRange(Ids(res.Arr("destroyed")));
            state = res.Str("newState") ?? state;
            if (!res.IsTrue("hasMoreChanges"))
            {
                return Result<ChangeSet>.Ok(new ChangeSet([.. changed.Distinct(StringComparer.Ordinal)], destroyed, state));
            }
        }
    }

    private static IEnumerable<string> Ids(JsonArray? a) => (a ?? []).Select(x => x?.GetValue<string>()).OfType<string>();

    private sealed record Applied(List<string> Touched, bool Calendars);

    private sealed record ChangeSet(List<string> Changed, List<string> Destroyed, string NewState);
}
