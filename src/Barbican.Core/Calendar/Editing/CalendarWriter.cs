// Mirrors: Rust cal_editor.rs:1037 save, :1115 scope_go, :1177 delete, calendar.rs:815 create_in, :830 toggle_task, :889 set_call, :921 set_task_due; Bulwark stores/calendar-store.ts (createEvent, updateEvent, deleteEvent), lib/recurrence-scope-edit.ts;
// JMAP Calendars CalendarEvent/set (create, update with JSON-pointer patches, destroy, sendSchedulingMessages), Stalwart crates/jmap/src/calendar_event/set.rs. B-CAL-013..017, 051, 052.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Calendar.Editing;

/// <summary>The default <see cref="ICalendarWriter"/>: every event and task write goes through here, with the plugin hooks, the optimistic change of the <see cref="ICalendarStore"/> and its rollback.</summary>
public sealed partial class CalendarWriter(IAccountManager accounts, ICalendarStore store, IHookBus hooks, IIdentityService? identities = null) : ICalendarWriter
{
    /// <summary>The write the user asked for was stopped by a plugin (an intercept hook said no).</summary>
    internal static readonly ValidationError StoppedByPlugin = new("A plugin stopped this change.");

    public async Task<Result<string>> SaveEventAsync(EventWrite write, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(write);
        var client = ClientFor(write.AccountKey);
        if (!client.IsOk)
        {
            return Result<string>.Fail(client.Error!);
        }

        if (write.EventId is null)
        {
            return await CreateEventAsync(client.Value!, write.AccountKey, write.Data, write.SendSchedulingMessages, ct).ConfigureAwait(false);
        }

        var master = StoredEvent(write.AccountKey, write.EventId);
        if (write.Scope is EditScope.Occurrence or EditScope.Following && (master is null || write.RecurrenceId is null))
        {
            return Result<string>.Fail(new ValidationError("This event is no longer in the calendar."));
        }

        return write.Scope switch
        {
            EditScope.Occurrence => await SaveOccurrenceAsync(client.Value!, write, master!, ct).ConfigureAwait(false),
            EditScope.Following => await SaveFollowingAsync(client.Value!, write, master!, ct).ConfigureAwait(false),
            _ => await SaveWholeAsync(client.Value!, write, master, ct).ConfigureAwait(false),
        };
    }

    public async Task<Result<Unit>> DeleteEventAsync(string accountKey, string eventId, EditScope scope, string? recurrenceId, bool notify, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(eventId);
        var client = ClientFor(accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        var master = StoredEvent(accountKey, eventId);
        var title = master?.Str("title") ?? "";
        if (!await hooks.AllowAsync(master.IsTask() ? HookNames.OnBeforeTaskDelete : HookNames.OnBeforeEventDelete, () => new DeleteHookPayload(eventId, title), ct).ConfigureAwait(false))
        {
            return Result<Unit>.Fail(StoppedByPlugin);
        }

        // "this and following" on the first occurrence is the whole series (Rust scope_go)
        var first = master is not null && recurrenceId is not null && CalendarTime.ParseLocal(master.Str("start")) == CalendarTime.ParseLocal(recurrenceId);
        var effective = scope == EditScope.Following && first ? EditScope.All : scope;
        var send = notify && (master is null || (EventEditor.UserOrganizes(master, await OwnAddressesAsync(accountKey, ct).ConfigureAwait(false)) && EventEditor.AttendeesOf(master).Count > 0));
        var result = effective switch
        {
            EditScope.Occurrence when master is not null && recurrenceId is not null => await UpdateEventAsync(client.Value!, accountKey, eventId, master, EventEditor.OverridePatch(master, recurrenceId, new JsonObject { ["excluded"] = true }), send, ct).ConfigureAwait(false),
            EditScope.Following when master is not null && recurrenceId is not null => await UpdateEventAsync(client.Value!, accountKey, eventId, master, EventEditor.TruncatePatch(master, recurrenceId).Truncate, send, ct).ConfigureAwait(false),
            _ => await DestroyEventAsync(client.Value!, accountKey, eventId, master, send, ct).ConfigureAwait(false),
        };
        if (result.IsOk)
        {
            hooks.Fire(master.IsTask() ? HookNames.OnTaskDelete : HookNames.OnAfterEventDelete, () => new DeleteHookPayload(eventId, title));
            await RefreshAsync(accountKey, ct).ConfigureAwait(false);
        }

        return result.Map(_ => default(Unit));
    }

    public async Task<Result<Unit>> MoveAsync(string accountKey, string occurrenceId, DateTime newStart, TimeSpan duration, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        var client = ClientFor(accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        var id = CalendarIds.BaseId(occurrenceId);
        var rid = CalendarIds.RecurrenceIdOf(occurrenceId);
        if (StoredEvent(accountKey, id) is not { } ev)
        {
            return Result<Unit>.Fail(new ValidationError("This event is no longer in the calendar."));
        }

        var allDay = ev.IsTrue("showWithoutTime");
        var onEventClock = allDay ? newStart : TimeZones.LocalToZone(newStart, ev.Str("timeZone"));
        var seconds = (long)duration.TotalSeconds;
        var durationText = CalendarTime.DurationString(seconds);
        var (currentStart, currentSeconds) = CurrentSlot(ev, rid);
        if (currentStart == onEventClock && currentSeconds == seconds)
        {
            return Result<Unit>.Ok(default);
        }

        var patch = rid is not null && ev.Arr("recurrenceRules") is { Count: > 0 }
            ? EventEditor.OverridePatch(ev, rid, EventEditor.OccurrenceOverride(ev, rid, new JsonObject { ["duration"] = durationText }, onEventClock))
            : new JsonObject { ["start"] = CalendarTime.FormatLocal(onEventClock), ["duration"] = durationText };
        var send = EventEditor.AttendeesOf(ev).Count > 0 && EventEditor.UserOrganizes(ev, await OwnAddressesAsync(accountKey, ct).ConfigureAwait(false));
        return await UpdateEventAsync(client.Value!, accountKey, id, ev, patch, send, ct).ConfigureAwait(false);
    }

    public async Task<Result<string>> CreateAsync(string accountKey, string calendarId, JsonObject data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(calendarId);
        ArgumentNullException.ThrowIfNull(data);
        var client = ClientFor(accountKey);
        if (!client.IsOk)
        {
            return Result<string>.Fail(client.Error!);
        }

        var copy = (JsonObject)data.DeepClone();
        copy["calendarIds"] = new JsonObject { [calendarId] = true };
        return await CreateEventAsync(client.Value!, accountKey, copy, false, ct).ConfigureAwait(false);
    }

    public async Task<Result<Unit>> UpdateAsync(string accountKey, string id, JsonObject patch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(patch);
        var client = ClientFor(accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        var ev = StoredEvent(accountKey, id);
        var wasDone = ev.IsDone();
        var result = await UpdateEventAsync(client.Value!, accountKey, id, ev, patch, false, ct).ConfigureAwait(false);
        if (result.IsOk && ev.IsTask() && patch.TryGetPropertyValue("progress", out var progress) && string.Equals((string?)progress, "completed", StringComparison.Ordinal) != wasDone)
        {
            hooks.Fire(HookNames.OnTaskToggleComplete, () => new TaskToggledPayload(id, string.Equals((string?)progress, "completed", StringComparison.Ordinal)));
        }

        return result.Map(_ => default(Unit));
    }

    public async Task<Result<Unit>> DestroyAsync(string accountKey, string id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(id);
        return await DeleteEventAsync(accountKey, id, EditScope.WholeEvent, null, false, ct).ConfigureAwait(false);
    }

    /// <summary>onCheckEventConflicts: what plugins add while the start or end changes in the editor.</summary>
    public async Task<IReadOnlyList<string>> CheckConflictsAsync(JsonNode ev, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ev);
        var shaped = await hooks.ShapeAsync(HookNames.OnCheckEventConflicts, new ConflictCheckPayload(ev, []), ct).ConfigureAwait(false);
        return shaped.Warnings;
    }
}
