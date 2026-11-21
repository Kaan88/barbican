// Mirrors: Rust invite.rs:195 parse, :210 stored, :230 answer; Bulwark lib/calendar-rsvp.ts, calendar-invitation.ts, calendar-cancellation.ts, components/email/calendar-invitation-banner.tsx; RFC 5546 (REQUEST, REPLY, COUNTER, CANCEL); JMAP Calendars CalendarEvent/parse and
// sendSchedulingMessages; Stalwart crates/jmap/src/calendar_event/{parse,set}.rs; hooks onBeforeEventRsvp, onEventRsvp. B-CAL-051; G-001; G-002; D-M4-10.

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Calendar.Invitations;

/// <summary>The default <see cref="IInvitationService"/>.</summary>
public sealed class InvitationService(IAccountManager accounts, ICalendarStore store, IHookBus hooks, IIdentityService? identities = null) : IInvitationService
{
    public async Task<Result<ParsedInvitation>> ParseAsync(string accountKey, string blobId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(blobId);
        var client = CalendarJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<ParsedInvitation>.Fail(client.Error!);
        }

        var args = new JsonObject { ["accountId"] = client.Value!.Session.AccountFor(CalendarJmap.Capability), ["blobIds"] = new JsonArray(blobId) };
        var r = await client.Value.CallAsync(JmapRequest.Of(JmapRequest.Call("CalendarEvent/parse", args, "p")), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<ParsedInvitation>.Fail(r.Error!);
        }

        var response = r.Value!.Args("p", "CalendarEvent/parse");
        if (!response.IsOk)
        {
            return Result<ParsedInvitation>.Fail(response.Error!);
        }

        if (response.Value!["parsed"]?[blobId]?[0] is not { } ev)
        {
            return Result<ParsedInvitation>.Fail(new ValidationError("There is no event in this message."));
        }

        string? raw = null;
        var download = await client.Value.DownloadAsync(client.Value.Session.MailAccountId, blobId, "invite.ics", "text/calendar", ct).ConfigureAwait(false);
        if (download.IsOk)
        {
            var stream = download.Value!;
            await using (stream.ConfigureAwait(false))
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                raw = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            }
        }

        return Result<ParsedInvitation>.Ok(new ParsedInvitation(ev.DeepClone(), InvitationModel.Method(ev, raw)));
    }

    public async Task<Result<JsonNode?>> StoredAsync(string accountKey, string uid, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uid);
        var snapshot = store.Snapshot(accountKey);
        if (snapshot is null)
        {
            var loaded = await store.LoadAsync(accountKey, ct).ConfigureAwait(false);
            if (!loaded.IsOk)
            {
                return Result<JsonNode?>.Fail(loaded.Error!);
            }

            snapshot = loaded.Value!;
        }

        var found = snapshot.Events.FirstOrDefault(e => string.Equals(e.Str("uid"), uid, StringComparison.Ordinal) && CalendarIds.RecurrenceIdOf(e.Str("id") ?? "") is null && !e.IsTask());
        return Result<JsonNode?>.Ok(found?.DeepClone());
    }

    public async Task<Result<string>> AnswerAsync(string accountKey, JsonNode invitation, IReadOnlyList<string> mine, string status, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(status);
        var client = CalendarJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<string>.Fail(client.Error!);
        }

        var uid = invitation.Str("uid") ?? "";
        var stored = await StoredAsync(accountKey, uid, ct).ConfigureAwait(false);
        if (!stored.IsOk)
        {
            return Result<string>.Fail(stored.Error!);
        }

        var known = stored.Value;
        var eventId = known?.Str("id") ?? invitation.Str("id") ?? uid;
        if (!await hooks.AllowAsync(HookNames.OnBeforeEventRsvp, () => new RsvpHookPayload(eventId, status), ct).ConfigureAwait(false))
        {
            return Result<string>.Fail(new HookStoppedError("A plugin stopped the answer."));
        }

        var own = mine.Count > 0 ? mine : await CalendarJmap.OwnAddressesAsync(accounts, identities, accountKey, ct).ConfigureAwait(false);
        var answered = known is null
            ? await AddAnsweredAsync(client.Value!, accountKey, invitation, own, status, ct).ConfigureAwait(false)
            : await PatchAnswerAsync(client.Value!, known, own, status, ct).ConfigureAwait(false);
        if (!answered.IsOk)
        {
            return answered;
        }

        _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        var done = answered.Value!;
        hooks.Fire(HookNames.OnEventRsvp, () => new RsvpHookPayload(done, status));
        return answered;
    }

    public async Task<Result<string>> AddToCalendarAsync(string accountKey, JsonNode invitation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        var client = CalendarJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<string>.Fail(client.Error!);
        }

        var added = await CreateStoredCopyAsync(client.Value!, accountKey, invitation, null, ct).ConfigureAwait(false);
        if (added.IsOk)
        {
            _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        }

        return added;
    }

    public async Task<Result<Unit>> ApplyCounterAsync(string accountKey, JsonNode counter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(counter);
        var client = CalendarJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        var stored = await StoredAsync(accountKey, counter.Str("uid") ?? "", ct).ConfigureAwait(false);
        if (!stored.IsOk)
        {
            return Result<Unit>.Fail(stored.Error!);
        }

        if (stored.Value is not { } known)
        {
            return Result<Unit>.Fail(new ValidationError("This event is not in your calendar."));
        }

        var patch = new JsonObject();
        foreach (var key in new[] { "start", "timeZone", "duration", "showWithoutTime" })
        {
            if (counter[key] is { } v)
            {
                patch[key] = v.DeepClone();
            }
        }

        if (patch.Count == 0)
        {
            return Result<Unit>.Fail(new ValidationError("The proposal has no new time."));
        }

        // the guests are asked again about the new time (the organiser's own answer stays)
        var own = await CalendarJmap.OwnAddressesAsync(accounts, identities, accountKey, ct).ConfigureAwait(false);
        if (known["participants"] is { } participants)
        {
            patch["participants"] = EventEditor.AskedAgain(participants.DeepClone(), own).DeepClone();
        }

        var (account, serverId) = CalendarWriter.ServerRef(client.Value!.Session.AccountFor(CalendarJmap.Capability), known, known.Str("id") ?? "");
        var done = await CalendarJmap.UpdateOnServerAsync(client.Value!, account, serverId, patch, true, ct).ConfigureAwait(false);
        if (done.IsOk)
        {
            _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        }

        return done;
    }

    public async Task<Result<Unit>> RemoveCancelledAsync(string accountKey, string uid, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uid);
        var client = CalendarJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        var stored = await StoredAsync(accountKey, uid, ct).ConfigureAwait(false);
        if (!stored.IsOk)
        {
            return Result<Unit>.Fail(stored.Error!);
        }

        if (stored.Value is not { } known)
        {
            return Result<Unit>.Ok(default);
        }

        var (account, serverId) = CalendarWriter.ServerRef(client.Value!.Session.AccountFor(CalendarJmap.Capability), known, known.Str("id") ?? "");
        var done = await CalendarJmap.DestroyOnServerAsync(client.Value!, account, serverId, false, ct).ConfigureAwait(false);
        if (done.IsOk)
        {
            _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        }

        return done;
    }

    private static async Task<Result<string>> PatchAnswerAsync(IJmapClient client, JsonNode known, IReadOnlyList<string> own, string status, CancellationToken ct)
    {
        var me = InvitationModel.MyParticipant(known, own);
        if (me is null)
        {
            return Result<string>.Fail(new ValidationError("You are not a guest of this event."));
        }

        var id = known.Str("id") ?? "";
        var (account, serverId) = CalendarWriter.ServerRef(client.Session.AccountFor(CalendarJmap.Capability), known, id);
        var done = await CalendarJmap.UpdateOnServerAsync(client, account, serverId, InvitationActions.RsvpPatch(me.Id, status), true, ct).ConfigureAwait(false);
        return done.IsOk ? Result<string>.Ok(id) : Result<string>.Fail(done.Error!);
    }

    /// <summary>Not in the calendar yet: the copy is created with the answer already in it and the server sends the reply, one call (a failure leaves nothing behind).</summary>
    private async Task<Result<string>> AddAnsweredAsync(IJmapClient client, string accountKey, JsonNode invitation, IReadOnlyList<string> own, string status, CancellationToken ct)
    {
        var me = InvitationModel.MyParticipant(invitation, own);
        if (me is null)
        {
            return Result<string>.Fail(new ValidationError("You are not a guest of this event."));
        }

        return await CreateStoredCopyAsync(client, accountKey, invitation, (me.Id, status), ct).ConfigureAwait(false);
    }

    private async Task<Result<string>> CreateStoredCopyAsync(IJmapClient client, string accountKey, JsonNode invitation, (string Id, string Status)? answer, CancellationToken ct)
    {
        var calendar = DefaultCalendar(accountKey);
        if (calendar is null)
        {
            return Result<string>.Fail(new ValidationError("There is no calendar to add the event to."));
        }

        var (account, serverCalendar) = CalendarIds.SplitNs(calendar);
        account ??= client.Session.AccountFor(CalendarJmap.Capability);
        var copy = (JsonObject)InvitationActions.Importable(invitation, serverCalendar);
        copy["@type"] ??= "Event";
        if (answer is { } a && copy["participants"]?[a.Id] is JsonObject p)
        {
            p["participationStatus"] = a.Status;
        }

        return await CalendarJmap.CreateOnServerAsync(client, account, copy, answer is not null, ct).ConfigureAwait(false);
    }

    private string? DefaultCalendar(string accountKey)
    {
        var calendars = store.Snapshot(accountKey)?.Calendars ?? [];
        var chosen = calendars.FirstOrDefault(c => c.IsTrue("isDefault") && c.Str("_acc") is null) ?? calendars.FirstOrDefault(c => c.IsTrue("isDefault")) ?? calendars.FirstOrDefault(c => c.Str("_acc") is null);
        return chosen?.Str("id");
    }
}
