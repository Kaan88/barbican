// Calls shared by the contact store and writers: the client of a login that has contacts, the accounts that share theirs, and AddressBook/set and ContactCard/set with the per-object error as the failure.
// Mirrors Rust calendar.rs:254 calendar_accounts (for contacts: contacts_x.rs CAP) and cal_editor.rs:1086 create, :1092 update, :1103 destroy; RFC 8620 5.3 (set errors), RFC 9610; Stalwart crates/jmap/src/contact/set.rs, addressbook/set.rs.

using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Requests;

namespace Barbican.Core.Contacts;

internal static class ContactJmap
{
    public static Result<IJmapClient> ClientFor(IAccountManager accounts, string accountKey)
    {
        if (accounts.SessionFor(accountKey)?.Client is not { } client)
        {
            return Result<IJmapClient>.Fail(new ValidationError("Not signed in."));
        }

        return client.Session.Has(ContactOptions.Capability)
            ? Result<IJmapClient>.Ok(client)
            : Result<IJmapClient>.Fail(new ValidationError("This account has no contacts."));
    }

    /// <summary>The primary contacts account, then the accounts shared with the login that have the contacts capability.</summary>
    public static List<(string Account, string Name)> Accounts(JmapSession session, string primary)
    {
        var output = new List<(string, string)> { (primary, NameOf(session, primary)) };
        if (session.Raw.ValueKind == JsonValueKind.Object && session.Raw.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Object)
        {
            foreach (var a in accounts.EnumerateObject())
            {
                if (!string.Equals(a.Name, primary, StringComparison.Ordinal) && a.Value.TryGetProperty("accountCapabilities", out var caps) && caps.ValueKind == JsonValueKind.Object && caps.TryGetProperty(ContactOptions.Capability, out _))
                {
                    output.Add((a.Name, NameOf(session, a.Name)));
                }
            }
        }

        return output;
    }

    public static string NameOf(JmapSession session, string account) =>
        session.Raw.ValueKind == JsonValueKind.Object && session.Raw.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Object
        && accounts.TryGetProperty(account, out var a) && a.ValueKind == JsonValueKind.Object && a.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? account : account;

    /// <summary>One /set call; the response arguments as a <see cref="SetOutcome"/>.</summary>
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

    public static SetError? FirstFailure(SetOutcome outcome) => outcome.Failed.Count > 0 ? outcome.Failed[0] : null;

    public static async Task<Result<string>> CreateAsync(IJmapClient client, string type, string account, JsonObject data, CancellationToken ct, string? onSuccessDefault = null)
    {
        var args = new JsonObject { ["accountId"] = account, ["create"] = new JsonObject { ["c"] = data.DeepClone() } };
        if (onSuccessDefault is not null)
        {
            args["onSuccessSetIsDefault"] = onSuccessDefault;
        }

        var r = await SetAsync(client, type, args, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<string>.Fail(r.Error!);
        }

        return r.Value!.Created.TryGetValue("c", out var id) ? Result<string>.Ok(id) : Result<string>.Fail((JmapError?)FirstFailure(r.Value) ?? new ValidationError("The server did not create it."));
    }

    public static async Task<Result<Unit>> UpdateAsync(IJmapClient client, string type, string account, string serverId, JsonObject patch, CancellationToken ct)
    {
        var args = new JsonObject { ["accountId"] = account, ["update"] = new JsonObject { [serverId] = patch.DeepClone() } };
        var r = await SetAsync(client, type, args, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<Unit>.Fail(r.Error!);
        }

        return r.Value!.Updated.Contains(serverId, StringComparer.Ordinal) ? Result<Unit>.Ok(default) : Result<Unit>.Fail((JmapError?)FirstFailure(r.Value) ?? new ValidationError("The server did not change it."));
    }

    /// <summary>Several updates in chunks of maxObjectsInSet; the first object the server refuses stops the call and is the failure (what was done before it stays done).</summary>
    public static async Task<Result<Unit>> UpdateManyAsync(IJmapClient client, string type, string account, IReadOnlyList<(string Id, JsonObject Patch)> items, CancellationToken ct)
    {
        foreach (var chunk in items.Chunk(Math.Max(1, client.Session.Limits.MaxObjectsInSet)))
        {
            var args = new JsonObject { ["accountId"] = account, ["update"] = new JsonObject(chunk.Select(i => KeyValuePair.Create(i.Id, (JsonNode?)i.Patch.DeepClone()))) };
            var r = await SetAsync(client, type, args, ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<Unit>.Fail(r.Error!);
            }

            if (FirstFailure(r.Value!) is { } failed)
            {
                return Result<Unit>.Fail(failed);
            }
        }

        return Result<Unit>.Ok(default);
    }

    public static async Task<Result<Unit>> DestroyManyAsync(IJmapClient client, string type, string account, IReadOnlyList<string> ids, JsonObject? extra, CancellationToken ct)
    {
        foreach (var chunk in ids.Chunk(Math.Max(1, client.Session.Limits.MaxObjectsInSet)))
        {
            var args = new JsonObject { ["accountId"] = account, ["destroy"] = new JsonArray([.. chunk.Select(i => (JsonNode)JsonValue.Create(i))]) };
            foreach (var (k, v) in extra ?? [])
            {
                args[k] = v?.DeepClone();
            }

            var r = await SetAsync(client, type, args, ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<Unit>.Fail(r.Error!);
            }

            if (FirstFailure(r.Value!) is { } failed)
            {
                return Result<Unit>.Fail(failed);
            }
        }

        return Result<Unit>.Ok(default);
    }
}
