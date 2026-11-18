// Mirrors: Rust calendar.rs:872 set_color, :889 set_call; Bulwark components/calendar/calendar-sidebar-panel.tsx, create-calendar-modal.tsx; JMAP Calendars Calendar/set (create, update, destroy with onDestroyRemoveEvents), onSuccessSetIsDefault (J-072).
// I-304..I-309.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Requests;

namespace Barbican.Core.Calendar.Editing;

/// <summary>The default <see cref="ICalendarAdmin"/>: calendar (not event) writes, always to the account the calendar lives in.</summary>
public sealed class CalendarAdmin(IAccountManager accounts, ICalendarStore store) : ICalendarAdmin
{
    private const string CalendarsCapability = "urn:ietf:params:jmap:calendars";

    public async Task<Result<string>> CreateCalendarAsync(string accountKey, string name, string color, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Trim().Length == 0)
        {
            return Result<string>.Fail(new ValidationError("Give the calendar a name."));
        }

        var client = ClientFor(accountKey);
        if (!client.IsOk)
        {
            return Result<string>.Fail(client.Error!);
        }

        var calendar = new JsonObject { ["name"] = name.Trim(), ["color"] = color, ["isVisible"] = true, ["isSubscribed"] = true };
        var r = await SetAsync(client.Value!, client.Value!.Session.AccountFor(CalendarsCapability), new JsonObject { ["create"] = new JsonObject { ["c"] = calendar } }, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<string>.Fail(r.Error!);
        }

        if (!r.Value!.Created.TryGetValue("c", out var id))
        {
            return Result<string>.Fail((JmapError?)FirstFailure(r.Value) ?? new ValidationError("The server did not create the calendar."));
        }

        await RefreshAsync(accountKey, ct).ConfigureAwait(false);
        return Result<string>.Ok(id);
    }

    public async Task<Result<Unit>> UpdateCalendarAsync(string accountKey, string calendarId, JsonObject patch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(calendarId);
        ArgumentNullException.ThrowIfNull(patch);
        return await UpdateAsync(accountKey, calendarId, patch, null, ct).ConfigureAwait(false);
    }

    public async Task<Result<Unit>> SetDefaultAsync(string accountKey, string calendarId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(calendarId);
        return await UpdateAsync(accountKey, calendarId, [], true, ct).ConfigureAwait(false);
    }

    public async Task<Result<Unit>> DeleteCalendarAsync(string accountKey, string calendarId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(calendarId);
        var client = ClientFor(accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        var calendar = store.Snapshot(accountKey)?.Calendars.FirstOrDefault(c => string.Equals(c.Str("id"), calendarId, StringComparison.Ordinal));
        if (calendar.IsTrue("isDefault"))
        {
            return Result<Unit>.Fail(new ValidationError("The default calendar cannot be deleted."));
        }

        var (account, id) = Ref(client.Value!, calendarId);
        var args = new JsonObject { ["destroy"] = new JsonArray(id), ["onDestroyRemoveEvents"] = true };
        var r = await SetAsync(client.Value!, account, args, ct).ConfigureAwait(false);
        var result = Outcome(r, id, destroyed: true);
        if (result.IsOk)
        {
            await RefreshAsync(accountKey, ct).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<Result<Unit>> UpdateAsync(string accountKey, string calendarId, JsonObject patch, bool? makeDefault, CancellationToken ct)
    {
        var client = ClientFor(accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        var (account, id) = Ref(client.Value!, calendarId);
        var args = new JsonObject { ["update"] = new JsonObject { [id] = patch.DeepClone() } };
        if (makeDefault == true)
        {
            args["onSuccessSetIsDefault"] = id;
        }

        var result = Outcome(await SetAsync(client.Value!, account, args, ct).ConfigureAwait(false), id, destroyed: false);
        if (result.IsOk)
        {
            await RefreshAsync(accountKey, ct).ConfigureAwait(false);
        }

        return result;
    }

    private static SetError? FirstFailure(SetOutcome outcome) => outcome.Failed.Count > 0 ? outcome.Failed[0] : null;

    private static Result<Unit> Outcome(Result<SetOutcome> r, string id, bool destroyed)
    {
        if (!r.IsOk)
        {
            return Result<Unit>.Fail(r.Error!);
        }

        var done = destroyed ? r.Value!.Destroyed.Contains(id, StringComparer.Ordinal) : r.Value!.Updated.Contains(id, StringComparer.Ordinal);
        return done ? Result<Unit>.Ok(default) : Result<Unit>.Fail((JmapError?)FirstFailure(r.Value!) ?? new ValidationError("The server did not change the calendar."));
    }

    private static (string Account, string Id) Ref(IJmapClient client, string calendarId)
    {
        var (account, id) = CalendarIds.SplitNs(calendarId);
        return (account ?? client.Session.AccountFor(CalendarsCapability), id);
    }

    private Result<IJmapClient> ClientFor(string accountKey) => CalendarJmap.ClientFor(accounts, accountKey);

    private static async Task<Result<SetOutcome>> SetAsync(IJmapClient client, string account, JsonObject args, CancellationToken ct)
    {
        args["accountId"] = account;
        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("Calendar/set", args, "s")), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<SetOutcome>.Fail(r.Error!);
        }

        var response = r.Value!.Args("s", "Calendar/set");
        return response.IsOk ? Result<SetOutcome>.Ok(SetErrors.Read(response.Value)) : Result<SetOutcome>.Fail(response.Error!);
    }

    private async Task RefreshAsync(string accountKey, CancellationToken ct) => _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
}
