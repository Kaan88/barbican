// Mirrors: Bulwark lib/jmap/client.ts:5213-5287 and stores/account-security-store.ts (a /set failure becomes the server's own words); Rust jmap.rs:1311 set_error. Owner set-data. Internal helpers of the account-level services.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Reader;

namespace Barbican.Core.Settings.Account;

internal static class AccountCalls
{
    /// <summary>The signed-in session of an account, or the failure to show.</summary>
    internal static Result<IAccountSession> Session(IAccountManager accounts, string accountKey)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        return accounts.SessionFor(accountKey) is { } s ? Result<IAccountSession>.Ok(s) : Result<IAccountSession>.Fail(new ValidationError("The account is not signed in."));
    }

    /// <summary>One method call on the session's client, the arguments of its answer, or the method error.</summary>
    internal static Task<Result<JsonObject>> CallAsync(IAccountSession session, string method, JsonObject args, CancellationToken ct) =>
        ReaderCalls.CallAsync(session.Client, method, args, ct);

    /// <summary>The server's own words for a SetError object (no object id in front of it, unlike <see cref="SetErrors.From"/>).</summary>
    internal static JmapError SetFailure(JsonNode? error, string fallback)
    {
        if (error is null)
        {
            return new ValidationError(fallback);
        }

        var type = error.Str("type") ?? "error";
        var description = error.Str("description");
        var text = description is { Length: > 0 } ? description : SetErrors.Message(type, null, error.Strings("properties"));
        return new ValidationError(text.Length == 0 ? fallback : text);
    }

    /// <summary>The SetError of <paramref name="id"/> in the failure map <paramref name="map"/> ("notCreated", "notUpdated", "notDestroyed") of a /set answer, or null.</summary>
    internal static JsonNode? Failed(JsonObject answer, string map, string id) => answer[map] is JsonObject bad && bad[id] is { } e ? e : null;
}
