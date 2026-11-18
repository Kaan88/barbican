// The three scopes of a series edit, as Rust cal_editor.rs:1115 scope_go and Bulwark lib/recurrence-scope-edit.ts do them: this occurrence (an override), this and following (the series is cut and a new one starts, the old one put back when the second call fails),
// all (the series moves as far as the occurrence did).

using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Common;
using Barbican.Core.Jmap;

namespace Barbican.Core.Calendar.Editing;

public sealed partial class CalendarWriter
{
    /// <summary>The occurrence's new start on the series' own clock: the form works on its own zone, an override on the series'.</summary>
    private static DateTime NewStartOnSeriesClock(JsonNode master, JsonObject data)
    {
        var formStart = CalendarTime.ParseLocal(data.Str("start")) ?? default;
        var formAllDay = data["showWithoutTime"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        if (!string.Equals(data.Str("timeZone"), master.Str("timeZone"), StringComparison.Ordinal) && !master.IsTrue("showWithoutTime") && !formAllDay)
        {
            return TimeZones.LocalToZone(TimeZones.ZoneToLocal(formStart, data.Str("timeZone")), master.Str("timeZone"));
        }

        return formStart;
    }

    private async Task<Result<string>> SaveWholeAsync(IJmapClient client, EventWrite write, JsonNode? master, CancellationToken ct)
    {
        var id = write.EventId!;
        var patch = write.Data;
        if (write.Scope == EditScope.All && master is not null)
        {
            var data = (JsonObject)write.Data.DeepClone();
            var newStart = NewStartOnSeriesClock(master, data);
            data["start"] = CalendarTime.FormatLocal(newStart);
            var occurrenceStart = write.RecurrenceId is null ? newStart : CurrentSlot(master, write.RecurrenceId).Start;
            patch = EventEditor.SeriesUpdate(master, occurrenceStart, data);
        }

        var result = await UpdateEventAsync(client, write.AccountKey, id, master, patch, write.SendSchedulingMessages, ct).ConfigureAwait(false);
        return result.IsOk ? Result<string>.Ok(id) : Result<string>.Fail(result.Error!);
    }

    private async Task<Result<string>> SaveOccurrenceAsync(IJmapClient client, EventWrite write, JsonNode master, CancellationToken ct)
    {
        var rid = write.RecurrenceId!;
        var over = EventEditor.OccurrenceOverride(master, rid, write.Data, NewStartOnSeriesClock(master, write.Data));
        var patch = EventEditor.OverridePatch(master, rid, over);
        var result = await UpdateEventAsync(client, write.AccountKey, write.EventId!, master, patch, write.SendSchedulingMessages, ct).ConfigureAwait(false);
        return result.IsOk ? Result<string>.Ok(write.EventId!) : Result<string>.Fail(result.Error!);
    }

    private async Task<Result<string>> SaveFollowingAsync(IJmapClient client, EventWrite write, JsonNode master, CancellationToken ct)
    {
        var rid = write.RecurrenceId!;
        if (CalendarTime.ParseLocal(master.Str("start")) == CalendarTime.ParseLocal(rid))
        {
            // the first occurrence: "this and following" is the whole series
            return await SaveWholeAsync(client, write with { Scope = EditScope.All }, master, ct).ConfigureAwait(false);
        }

        var id = CalendarIds.BaseId(write.EventId!);
        var (truncate, restore) = EventEditor.TruncatePatch(master, rid);
        var next = EventEditor.FollowingSeries(master, rid, write.Data, await OwnAddressesAsync(write.AccountKey, ct).ConfigureAwait(false));
        next["start"] = CalendarTime.FormatLocal(CalendarTime.ParseLocal(write.Data.Str("start")) ?? default);
        var first = await UpdateKeepingStoreAsync(client, write.AccountKey, id, master, truncate, write.SendSchedulingMessages, ct).ConfigureAwait(false);
        if (!first.IsOk)
        {
            if (first.Previous is not null)
            {
                store.Restore(write.AccountKey, id, first.Previous);
            }

            return Result<string>.Fail(first.Result.Error!);
        }

        var (account, serverId) = ServerRef(client.Session.AccountFor("urn:ietf:params:jmap:calendars"), master, id);
        var created = await CreateEventAsync(client, write.AccountKey, next, write.SendSchedulingMessages, ct, account).ConfigureAwait(false);
        if (!created.IsOk)
        {
            // put the series back as it was; the first failure is what the user is told
            _ = await UpdateOnServerAsync(client, account, serverId, restore, false, ct).ConfigureAwait(false);
            if (first.Previous is not null)
            {
                store.Restore(write.AccountKey, id, first.Previous);
            }
        }

        return created;
    }
}
