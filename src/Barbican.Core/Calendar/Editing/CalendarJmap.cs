// Calls shared by the calendar writers: the client of a login that has calendars, and CalendarEvent/set for one create, update or destroy with its per-object error as the failure.
// Mirrors Rust cal_editor.rs:1086 create, :1092 update, :1103 destroy; RFC 8620 5.3 (set errors); Stalwart crates/jmap/src/calendar_event/set.rs.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Requests;

namespace Barbican.Core.Calendar.Editing;

internal static class CalendarJmap
{
    public const string Capability = "urn:ietf:params:jmap:calendars";

    public static Result<IJmapClient> ClientFor(IAccountManager accounts, string accountKey)
    {
        if (accounts.SessionFor(accountKey)?.Client is not { } client)
        {
            return Result<IJmapClient>.Fail(new ValidationError("Not signed in."));
        }

        return client.Session.Has(Capability)
            ? Result<IJmapClient>.Ok(client)
            : Result<IJmapClient>.Fail(new ValidationError("This account has no calendar."));
    }

    public static SetError? FirstFailure(SetOutcome outcome) => outcome.Failed.Count > 0 ? outcome.Failed[0] : null;

    public static async Task<Result<SetOutcome>> SetAsync(IJmapClient client, string type, JsonObject args, CancellationToken ct)
    {
        var method = type + "/set";
        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call(method, args, "s")), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<SetOutcome>.Fail(r.Error!);
        }

        var response = r.Value!.Args("s", method);
        return response.IsOk ? Result<SetOutcome>.Ok(SetErrors.Read(response.Value)) : Result<SetOutcome>.Fail(response.Error!);
    }

    public static JsonObject SetArgs(string account, bool send)
    {
        var args = new JsonObject { ["accountId"] = account };
        if (send)
        {
            args["sendSchedulingMessages"] = true;
        }

        return args;
    }

    public static async Task<Result<string>> CreateOnServerAsync(IJmapClient client, string account, JsonObject data, bool send, CancellationToken ct)
    {
        var args = SetArgs(account, send);
        args["create"] = new JsonObject { ["e"] = data.DeepClone() };
        var r = await SetAsync(client, "CalendarEvent", args, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<string>.Fail(r.Error!);
        }

        return r.Value!.Created.TryGetValue("e", out var id)
            ? Result<string>.Ok(id)
            : Result<string>.Fail((JmapError?)FirstFailure(r.Value) ?? new ValidationError("The server did not create the event."));
    }

    public static async Task<Result<Unit>> UpdateOnServerAsync(IJmapClient client, string account, string serverId, JsonObject patch, bool send, CancellationToken ct)
    {
        var body = (JsonObject)patch.DeepClone();
        if (body["calendarIds"] is { } ids)
        {
            body["calendarIds"] = CalendarIds.ServerCalendarIds(ids);
        }

        var args = SetArgs(account, send);
        args["update"] = new JsonObject { [serverId] = body };
        var r = await SetAsync(client, "CalendarEvent", args, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<Unit>.Fail(r.Error!);
        }

        return r.Value!.Updated.Contains(serverId, StringComparer.Ordinal)
            ? Result<Unit>.Ok(default)
            : Result<Unit>.Fail((JmapError?)FirstFailure(r.Value) ?? new ValidationError("The server did not change the event."));
    }

    public static async Task<Result<Unit>> DestroyOnServerAsync(IJmapClient client, string account, string serverId, bool send, CancellationToken ct)
    {
        var args = SetArgs(account, send);
        args["destroy"] = new JsonArray(serverId);
        var r = await SetAsync(client, "CalendarEvent", args, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<Unit>.Fail(r.Error!);
        }

        return r.Value!.Destroyed.Contains(serverId, StringComparer.Ordinal)
            ? Result<Unit>.Ok(default)
            : Result<Unit>.Fail((JmapError?)FirstFailure(r.Value) ?? new ValidationError("The server did not delete the event."));
    }

    /// <summary>Creates many objects in chunks of maxObjectsInSet (no scheduling messages); the outcome of every chunk summed: the ids made and a failure text per object that was refused.</summary>
    public static async Task<(int Created, IReadOnlyList<string> Failed, JmapError? Error)> CreateManyAsync(IJmapClient client, string account, IReadOnlyList<(string Key, JsonObject Data)> items, CancellationToken ct)
    {
        var created = 0;
        var failed = new List<string>();
        foreach (var chunk in items.Chunk(Math.Max(1, client.Session.Limits.MaxObjectsInSet)))
        {
            var args = SetArgs(account, false);
            args["create"] = new JsonObject(chunk.Select(i => KeyValuePair.Create(i.Key, (JsonNode?)i.Data.DeepClone())));
            var r = await SetAsync(client, "CalendarEvent", args, ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return (created, failed, r.Error);
            }

            created += r.Value!.Created.Count;
            failed.AddRange(r.Value.Failed.Select(f => f.Message));
        }

        return (created, failed, null);
    }

    /// <summary>Updates many events with their own patches in chunks (no scheduling messages); the failures as text.</summary>
    public static async Task<(IReadOnlyList<string> Failed, JmapError? Error)> UpdateManyAsync(IJmapClient client, string account, IReadOnlyList<(string Id, JsonObject Patch)> items, CancellationToken ct)
    {
        var failed = new List<string>();
        foreach (var chunk in items.Chunk(Math.Max(1, client.Session.Limits.MaxObjectsInSet)))
        {
            var args = SetArgs(account, false);
            args["update"] = new JsonObject(chunk.Select(i => KeyValuePair.Create(i.Id, (JsonNode?)i.Patch.DeepClone())));
            var r = await SetAsync(client, "CalendarEvent", args, ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return (failed, r.Error);
            }

            failed.AddRange(r.Value!.Failed.Select(f => f.Message));
        }

        return (failed, null);
    }

    /// <summary>Destroys many events in chunks (no scheduling messages); the failures as text.</summary>
    public static async Task<(IReadOnlyList<string> Failed, JmapError? Error)> DestroyManyAsync(IJmapClient client, string account, IReadOnlyList<string> ids, CancellationToken ct)
    {
        var failed = new List<string>();
        foreach (var chunk in ids.Chunk(Math.Max(1, client.Session.Limits.MaxObjectsInSet)))
        {
            var args = SetArgs(account, false);
            args["destroy"] = new JsonArray([.. chunk.Select(i => (JsonNode)JsonValue.Create(i))]);
            var r = await SetAsync(client, "CalendarEvent", args, ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return (failed, r.Error);
            }

            failed.AddRange(r.Value!.Failed.Select(f => f.Message));
        }

        return (failed, null);
    }

    /// <summary>The user's own addresses, lower case first the login then the identities (organiser detection, answers, counter-proposals).</summary>
    public static async Task<IReadOnlyList<string>> OwnAddressesAsync(IAccountManager accounts, Compose.IIdentityService? identities, string accountKey, CancellationToken ct)
    {
        var own = new List<string>();
        if (accounts.SessionFor(accountKey) is not { } session)
        {
            return own;
        }

        var user = session.Client.Session.Username;
        if (user.Contains('@', StringComparison.Ordinal))
        {
            own.Add(user);
        }

        if (identities is null)
        {
            return own;
        }

        var list = await identities.ListAsync(session.Client.Session.MailAccountId, ct).ConfigureAwait(false);
        foreach (var i in list.IsOk ? list.Value! : [])
        {
            if (i.Identity.Email.Contains('@', StringComparison.Ordinal) && !own.Contains(i.Identity.Email.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                own.Add(i.Identity.Email.Trim());
            }
        }

        return own;
    }
}
