// The server side of the writes: which client and account, the CalendarEvent/set calls, the hooks around them and the store's optimistic patch with its rollback.
// Mirrors Rust cal_editor.rs:1086 create, :1092 update, :1103 destroy and calendar.rs:236 server_ref; Stalwart crates/jmap/src/calendar_event/set.rs.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Calendar.Editing;

public sealed partial class CalendarWriter
{
    private const string CalendarsCapability = CalendarJmap.Capability;

    /// <summary>What an update did to the store: the event as it was (for <see cref="ICalendarStore.Restore"/>) and the outcome.</summary>
    private sealed record UpdateOutcome(Result<Unit> Result, JsonNode? Previous)
    {
        public bool IsOk => Result.IsOk;
    }

    private Result<IJmapClient> ClientFor(string accountKey) => CalendarJmap.ClientFor(accounts, accountKey);

    private JsonNode? StoredEvent(string accountKey, string eventId)
    {
        var id = CalendarIds.BaseId(eventId);
        return store.Snapshot(accountKey)?.Events.FirstOrDefault(e => string.Equals(e.Str("id"), id, StringComparison.Ordinal));
    }

    /// <summary>The account and server id of an event: a shared account's events carry their own (<c>_acc</c>, <c>_rid</c>), else the namespace of the id, else the primary calendar account.</summary>
    internal static (string Account, string ServerId) ServerRef(string primary, JsonNode? ev, string id)
    {
        if (ev?.Str("_rid") is { } rid)
        {
            return (ev.Str("_acc") ?? primary, rid);
        }

        var (account, serverId) = CalendarIds.SplitNs(CalendarIds.BaseId(id));
        return (account ?? primary, serverId);
    }

    private Task<IReadOnlyList<string>> OwnAddressesAsync(string accountKey, CancellationToken ct) => CalendarJmap.OwnAddressesAsync(accounts, identities, accountKey, ct);

    private async Task RefreshAsync(string accountKey, CancellationToken ct)
    {
        // the push delta confirms every write; asking at once makes a create or a delete show without waiting for it
        _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
    }

    /// <summary>Creates an event or a task: hooks, the new object with server ids, the call, then the store catches up.</summary>
    private async Task<Result<string>> CreateEventAsync(IJmapClient client, string accountKey, JsonObject source, bool send, CancellationToken ct, string? account = null)
    {
        var data = (JsonObject)source.DeepClone();
        data["@type"] ??= "Event";
        foreach (var key in new[] { "recurrenceOverrides", "excludedRecurrenceRules", "replyTo" })
        {
            data.Remove(key);
        }

        foreach (var key in data.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList())
        {
            data.Remove(key);
        }

        var task = data.IsTask();
        var calendar = data["calendarIds"] is JsonObject ids && ids.Count > 0 ? ids.First().Key : "";
        var primary = client.Session.AccountFor(CalendarsCapability);
        account ??= CalendarIds.SplitNs(calendar).Account ?? primary;
        data["calendarIds"] = CalendarIds.ServerCalendarIds(data["calendarIds"] ?? new JsonObject());
        if (!await hooks.AllowAsync(task ? HookNames.OnBeforeTaskCreate : HookNames.OnBeforeEventCreate, () => new EventHookPayload(null, data.DeepClone()), ct).ConfigureAwait(false))
        {
            return Result<string>.Fail(StoppedByPlugin);
        }

        if (!task)
        {
            var shaped = await hooks.ShapeAsync(HookNames.OnCreatingEvent, new CreatingEventPayload(data.DeepClone(), data.DeepClone()), ct).ConfigureAwait(false);
            data = shaped.ServerEvent as JsonObject ?? data;
        }

        var created = await CalendarJmap.CreateOnServerAsync(client, account, data, send, ct).ConfigureAwait(false);
        if (created.IsOk)
        {
            var id = created.Value!;
            hooks.Fire(task ? HookNames.OnTaskCreate : HookNames.OnAfterEventCreate, () => new EventHookPayload(id, data.DeepClone()));
            await RefreshAsync(accountKey, ct).ConfigureAwait(false);
        }

        return created;
    }

    /// <summary>Updates one event or task: hooks, the store's optimistic patch, the call, and the store put back when the server refuses.</summary>
    private async Task<Result<Unit>> UpdateEventAsync(IJmapClient client, string accountKey, string eventId, JsonNode? ev, JsonObject patch, bool send, CancellationToken ct)
    {
        var outcome = await UpdateKeepingStoreAsync(client, accountKey, eventId, ev, patch, send, ct).ConfigureAwait(false);
        if (!outcome.IsOk && outcome.Previous is not null)
        {
            store.Restore(accountKey, CalendarIds.BaseId(eventId), outcome.Previous);
        }

        return outcome.Result;
    }

    private async Task<UpdateOutcome> UpdateKeepingStoreAsync(IJmapClient client, string accountKey, string eventId, JsonNode? ev, JsonObject patch, bool send, CancellationToken ct)
    {
        var task = ev.IsTask();
        var id = CalendarIds.BaseId(eventId);
        var (account, serverId) = ServerRef(client.Session.AccountFor(CalendarsCapability), ev, id);
        if (!await hooks.AllowAsync(task ? HookNames.OnBeforeTaskUpdate : HookNames.OnBeforeEventUpdate, () => new UpdateHookPayload(id, patch.DeepClone()), ct).ConfigureAwait(false))
        {
            return new UpdateOutcome(Result<Unit>.Fail(StoppedByPlugin), null);
        }

        var server = patch;
        if (!task)
        {
            var shaped = await hooks.ShapeAsync(HookNames.OnUpdatingEvent, new UpdatingEventPayload(patch.DeepClone(), patch.DeepClone(), id), ct).ConfigureAwait(false);
            server = shaped.ServerUpdates as JsonObject ?? patch;
        }

        var previous = store.Patch(accountKey, id, (JsonObject)patch.DeepClone());
        var result = await CalendarJmap.UpdateOnServerAsync(client, account, serverId, server, send, ct).ConfigureAwait(false);
        if (result.IsOk)
        {
            hooks.Fire(task ? HookNames.OnTaskUpdate : HookNames.OnAfterEventUpdate, () => new EventHookPayload(id, StoredEvent(accountKey, id)?.DeepClone() ?? patch.DeepClone()));
        }

        return new UpdateOutcome(result, previous);
    }

    private static async Task<Result<Unit>> DestroyEventAsync(IJmapClient client, string accountKey, string eventId, JsonNode? ev, bool send, CancellationToken ct)
    {
        var (account, serverId) = ServerRef(client.Session.AccountFor(CalendarsCapability), ev, eventId);
        var result = await CalendarJmap.DestroyOnServerAsync(client, account, serverId, send, ct).ConfigureAwait(false);
        _ = accountKey;
        return result;
    }

    /// <summary>The slot an occurrence has now on the event's own clock (its override's start and duration, else the series'), and its length in seconds.</summary>
    private static (DateTime Start, long Seconds) CurrentSlot(JsonNode ev, string? rid)
    {
        var over = rid is null ? null : ev.Obj("recurrenceOverrides")?[rid];
        var start = CalendarTime.ParseLocal(over.Str("start") ?? rid ?? ev.Str("start")) ?? default;
        var duration = over.Str("duration") ?? ev.Str("duration");
        return (start, CalendarTime.DurationSeconds(duration) ?? 0);
    }
}
