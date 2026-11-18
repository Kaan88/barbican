// Mirrors: Rust cal_extra.rs:467 parse_ics, :506 import_begin, :522 import_choose, :553 import_run; Bulwark components/calendar/ical-import-modal.tsx; hooks onBeforeEventsImport, onEventsImport (lib/plugin-hook-catalog.ts:178-:179); B-CAL-045; G-004.
// The file is read by IcsReader (D-M4-E01); a file it finds nothing in goes to the server's CalendarEvent/parse (Stalwart crates/jmap/src/calendar_event/parse.rs).

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Calendar.Editing;

/// <summary>The default <see cref="IIcsImporter"/>.</summary>
public sealed class IcsImporter(IAccountManager accounts, ICalendarStore store, IHookBus hooks) : IIcsImporter
{
    /// <summary>The biggest file read (Rust cal_extra.rs:471).</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    public async Task<Result<IReadOnlyList<JsonNode>>> ParseAsync(string accountKey, ReadOnlyMemory<byte> ics, CancellationToken ct)
    {
        if (ics.Length > MaxBytes)
        {
            return Result<IReadOnlyList<JsonNode>>.Fail(new ValidationError("The file is larger than 5 MB."));
        }

        var local = IcsReader.Parse(Encoding.UTF8.GetString(ics.Span));
        if (local.Count > 0)
        {
            return Result<IReadOnlyList<JsonNode>>.Ok(local);
        }

        var viaServer = await ServerParseAsync(accountKey, ics, ct).ConfigureAwait(false);
        if (!viaServer.IsOk)
        {
            return viaServer;
        }

        return viaServer.Value!.Count > 0 ? viaServer : Result<IReadOnlyList<JsonNode>>.Fail(new ValidationError("There are no events in this file."));
    }

    public async Task<Result<int>> ImportAsync(string accountKey, string calendarId, IReadOnlyList<JsonNode> events, bool keepAlerts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(calendarId);
        ArgumentNullException.ThrowIfNull(events);
        var client = CalendarJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<int>.Fail(client.Error!);
        }

        if (!await hooks.AllowAsync(HookNames.OnBeforeEventsImport, () => new ImportCountPayload(calendarId, events.Count), ct).ConfigureAwait(false))
        {
            return Result<int>.Fail(new ValidationError("A plugin stopped the import."));
        }

        var (account, serverCalendar) = CalendarIds.SplitNs(calendarId);
        account ??= client.Value!.Session.AccountFor(CalendarJmap.Capability);
        var items = new List<(string Key, JsonObject Data)>();
        foreach (var source in events)
        {
            var data = (JsonObject)ImportRules.Sanitize(source, serverCalendar);
            if (!keepAlerts)
            {
                data.Remove("alerts");
            }

            items.Add(("i" + items.Count.ToString(CultureInfo.InvariantCulture), data));
        }

        var (created, failed, error) = await CalendarJmap.CreateManyAsync(client.Value!, account, items, ct).ConfigureAwait(false);
        _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        var skipped = items.Count - created;
        hooks.Fire(HookNames.OnEventsImport, () => new ImportDonePayload(calendarId, created, skipped));
        if (error is not null && created == 0)
        {
            return Result<int>.Fail(error);
        }

        return created == 0 && failed.Count > 0
            ? Result<int>.Fail(new ValidationError(failed[0]))
            : Result<int>.Ok(created);
    }

    private async Task<Result<IReadOnlyList<JsonNode>>> ServerParseAsync(string accountKey, ReadOnlyMemory<byte> ics, CancellationToken ct)
    {
        var client = CalendarJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<IReadOnlyList<JsonNode>>.Fail(client.Error!);
        }

        var uploaded = await new MailWriter(client.Value!, client.Value!.Session.MailAccountId).UploadAsync(ics, "text/calendar", ct).ConfigureAwait(false);
        if (!uploaded.IsOk)
        {
            return Result<IReadOnlyList<JsonNode>>.Fail(uploaded.Error!);
        }

        var blob = uploaded.Value!.BlobId;
        var args = new JsonObject { ["accountId"] = client.Value.Session.AccountFor(CalendarJmap.Capability), ["blobIds"] = new JsonArray(blob) };
        var r = await client.Value.CallAsync(JmapRequest.Of(JmapRequest.Call("CalendarEvent/parse", args, "p")), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<IReadOnlyList<JsonNode>>.Fail(r.Error!);
        }

        var response = r.Value!.Args("p", "CalendarEvent/parse");
        if (!response.IsOk)
        {
            return Result<IReadOnlyList<JsonNode>>.Fail(response.Error!);
        }

        var list = new List<JsonNode>();
        foreach (var ev in response.Value!["parsed"]?[blob] as JsonArray ?? [])
        {
            if (ev is not null)
            {
                list.Add(ev.DeepClone());
            }
        }

        return Result<IReadOnlyList<JsonNode>>.Ok(list);
    }
}
