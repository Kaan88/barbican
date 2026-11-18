// The signed-in JMAP client of an account key and the folder reads the srv-data services share (Mailbox/get for roles and folder paths, Inbox metadata). Owner srv-data.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Reader;

namespace Barbican.Core.Filters.Rules;

internal static class FilterAccess
{
    /// <summary>The "A plugin stopped this change." failure of an intercept hook.</summary>
    internal static readonly ValidationError StoppedByPlugin = new("A plugin stopped this change.");

    public static Result<IJmapClient> ClientFor(IAccountManager accounts, string accountKey)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(accountKey);
        return accounts.SessionFor(accountKey)?.Client is { } client
            ? Result<IJmapClient>.Ok(client)
            : Result<IJmapClient>.Fail(new ValidationError("The account is not signed in."));
    }

    /// <summary>Every folder of the mail account (id, name, parentId, role, isSubscribed).</summary>
    public static async Task<Result<IReadOnlyList<JsonNode>>> MailboxesAsync(IJmapClient client, CancellationToken ct)
    {
        var args = new JsonObject
        {
            ["accountId"] = client.Session.MailAccountId,
            ["ids"] = null,
            ["properties"] = new JsonArray("id", "name", "parentId", "role", "isSubscribed"),
        };
        var r = await ReaderCalls.CallAsync(client, "Mailbox/get", args, ct).ConfigureAwait(false);
        return r.IsOk
            ? Result<IReadOnlyList<JsonNode>>.Ok([.. (r.Value!["list"] as JsonArray ?? []).OfType<JsonNode>()])
            : Result<IReadOnlyList<JsonNode>>.Fail(r.Error!);
    }
}
