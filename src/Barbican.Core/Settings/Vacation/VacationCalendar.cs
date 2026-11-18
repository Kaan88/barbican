// Mirrors Bulwark lib/vacation-calendar.ts (UID, ownership and create/update/end/destroy decisions),
// RFC 8984 CalendarEvent; Stalwart crates/jmap/src/calendar_event/set.rs. G-022; owner srv-data.
using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Jmap;
using Barbican.Core.Settings.Folders;

namespace Barbican.Core.Settings.Vacation;

internal sealed class VacationCalendar(ICalendarStore store, ICalendarWriter writer, TimeProvider time)
{
    private const string RecordKey = "vacationCalendarBlocks";
    private const string Marker = "bulwark-out-of-office";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly HashSet<string> _loaded = new(StringComparer.Ordinal);

    #pragma warning disable MA0051 // The ownership decisions must be evaluated together against one snapshot.
    public async Task SyncAsync(string accountKey, IJmapClient client, VacationSettings reply, VacationSettings? previous, bool load, CancellationToken ct)
    {
        if (!client.Session.Has(CalendarJmap.Capability)) return;
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (load && !_loaded.Add(accountKey)) return;
            var snapshot = await store.LoadAsync(accountKey, ct).ConfigureAwait(false);
            if (!snapshot.IsOk) return;
            var account = client.Session.AccountFor(CalendarJmap.Capability);
            var found = snapshot.Value!.Events.FirstOrDefault(e => string.Equals(e.Str("uid"), "bulwark-ooo-" + account, StringComparison.Ordinal) && (e.Str("_acc") is null || string.Equals(e.Str("_acc"), account, StringComparison.Ordinal)));
            var span = Span(found);
            var last = await ReadLastAsync(client, account, ct).ConfigureAwait(false);
            var from = Boundary(reply.From, false);
            var to = Boundary(reply.To, true);
            var now = time.GetUtcNow();
            var wanted = reply.BlockCalendar && reply.Enabled && from is not null && to > from;
            var ranOut = reply.Enabled && to <= now;
            if (found is not null)
            {
                if (span is null) return;
                var matchesLast = last is not null && Same(last.Value.Start, span.Value.Start) && Same(last.Value.End, span.Value.End);
                var matchesPrevious = previous is not null && Same(Boundary(previous.From, false), span.Value.Start) && Same(Boundary(previous.To, true), span.Value.End);
                var markedOnLoad = load && last is null && found["keywords"]?[Marker]?.GetValue<bool>() == true;
                if (!matchesLast && !matchesPrevious && !markedOnLoad) return;
                var id = found.Str("id");
                if (id is null) return;
                if (wanted && !ranOut)
                {
                    if (Same(from, span.Value.Start) && Same(to, span.Value.End)) return;
                    var patch = Times(from!.Value, to!.Value);
                    patch["title"] = Title(reply);
                    patch["keywords/" + Marker] = true;
                    if ((await writer.UpdateAsync(accountKey, id, patch, ct).ConfigureAwait(false)).IsOk) await RememberAsync(client, account, from.Value, to.Value, ct).ConfigureAwait(false);
                }
                else if (!ranOut && span.Value.End > now)
                {
                    if (span.Value.Start > now)
                    {
                        if ((await writer.DestroyAsync(accountKey, id, ct).ConfigureAwait(false)).IsOk) await RememberAsync(client, account, null, null, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        var end = DateTimeOffset.FromUnixTimeSeconds(Math.Max(span.Value.Start.ToUnixTimeSeconds() + 60, ((now.ToUnixTimeSeconds() + 59) / 60) * 60));
                        if ((await writer.UpdateAsync(accountKey, id, Times(span.Value.Start, end), ct).ConfigureAwait(false)).IsOk) await RememberAsync(client, account, span.Value.Start, end, ct).ConfigureAwait(false);
                    }
                }
                return;
            }
            if (!wanted || ranOut || (load && last is not null && Same(from, last.Value.Start) && Same(to, last.Value.End))) return;
            var calendars = snapshot.Value.Calendars.Where(c => c.Str("_acc") is null || string.Equals(c.Str("_acc"), account, StringComparison.Ordinal)).ToList();
            var calendar = calendars.FirstOrDefault(c => c["isDefault"]?.GetValue<bool>() == true) ?? calendars.FirstOrDefault();
            if (calendar?.Str("id") is not { } calendarId) return;
            var rights = calendar["myRights"];
            if (rights is not null && rights["mayWriteAll"]?.GetValue<bool>() != true && rights["mayWriteOwn"]?.GetValue<bool>() != true) return;
            var data = Times(from!.Value, to!.Value);
            data["@type"] = "Event";
            data["uid"] = "bulwark-ooo-" + account;
            data["title"] = Title(reply);
            data["freeBusyStatus"] = "busy";
            data["privacy"] = "public";
            data["status"] = "confirmed";
            data["keywords"] = new JsonObject { [Marker] = true };
            if ((await writer.CreateAsync(accountKey, calendarId, data, ct).ConfigureAwait(false)).IsOk) await RememberAsync(client, account, from.Value, to.Value, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    #pragma warning restore MA0051

    private static bool Same(DateTimeOffset? a, DateTimeOffset b) => a is not null && Math.Abs((a.Value - b).TotalSeconds) <= 1;

    private static string Title(VacationSettings s) => string.IsNullOrWhiteSpace(s.Subject) ? "Out of office" : s.Subject.Trim();
    private static DateTimeOffset? Boundary(DateOnly? day, bool end) => day is null ? null : new DateTimeOffset(day.Value.ToDateTime(end ? new TimeOnly(23, 59, 59) : TimeOnly.MinValue), TimeSpan.Zero);
    private static JsonObject Times(DateTimeOffset start, DateTimeOffset end) => new()
    {
        ["start"] = start.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        ["duration"] = CalendarTime.DurationString((long)Math.Round((end - start).TotalMinutes, MidpointRounding.AwayFromZero) * 60),
        ["timeZone"] = "Etc/UTC",
        ["showWithoutTime"] = false,
    };

    private static (DateTimeOffset Start, DateTimeOffset End)? Span(JsonNode? ev)
    {
        if (ev is null || CalendarTime.ParseLocal(ev.Str("start")) is not { } wall || CalendarTime.DurationSeconds(ev.Str("duration")) is not { } seconds) return null;
        var allDay = ev["showWithoutTime"]?.GetValue<bool>() == true;
        var zone = allDay ? null : ev.Str("timeZone");
        var start = DateTimeOffset.FromUnixTimeMilliseconds(TimeZones.ZoneToUtcMs(wall, zone));
        var end = allDay ? DateTimeOffset.FromUnixTimeMilliseconds(TimeZones.ZoneToUtcMs(wall.AddDays(seconds / 86400), null)) : start.AddSeconds(seconds);
        return (start, end);
    }

    private static async Task<(DateTimeOffset Start, DateTimeOffset End)?> ReadLastAsync(IJmapClient client, string account, CancellationToken ct)
    {
        var read = await FolderAdminMetadata.ReadAsync(client, ct).ConfigureAwait(false);
        var entry = read.IsOk ? FolderAdminMetadata.ValueOf(read.Value.Metadata, RecordKey)?[account] : null;
        return DateTimeOffset.TryParse(entry.Str("start"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start) && DateTimeOffset.TryParse(entry.Str("end"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var end) ? (start, end) : null;
    }

    private async Task RememberAsync(IJmapClient client, string account, DateTimeOffset? start, DateTimeOffset? end, CancellationToken ct)
    {
        _ = await FolderAdminMetadata.UpdateAsync(client, meta =>
        {
            var map = FolderAdminMetadata.ValueOf(meta, RecordKey)?.DeepClone() as JsonObject ?? [];
            if (start is null || end is null) map.Remove(account);
            else map[account] = new JsonObject { ["start"] = start.Value.ToString("O", CultureInfo.InvariantCulture), ["end"] = end.Value.ToString("O", CultureInfo.InvariantCulture) };
            return new JsonObject { ["metadata/" + FolderAdminMetadata.ValueKey(RecordKey)] = FolderAdminMetadata.Entry(RecordKey, map, time.GetUtcNow()) };
        }, ct).ConfigureAwait(false);
    }
}
