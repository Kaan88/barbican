// Mirrors: Rust jmap.rs:831 identities (the submission account), :845 build_email, role_ids (Drafts and Sent by role); RFC 8621 section 1.3.2 (the submission capability can live in another account than mail).
// One resolved account of a composer: the session, the mail account (drafts, sent, blobs) and the submission account (identities, submissions). Owner send-pipeline.

using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Reader;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Barbican.Core.Compose;

/// <summary>A signed-in account as the send pipeline uses it. <c>ComposeDraft.AccountId</c> is the account key of <see cref="IAccountManager"/>.</summary>
internal sealed class ComposeContext
{
    private static readonly ConditionalWeakTable<IJmapClient, ConcurrentDictionary<string, MailboxRoles>> RoleCache = new();

    private ComposeContext(IAccountSession session)
    {
        Session = session;
        Account = session.Client.Session.MailAccountId;
        SubmissionAccount = session.Client.Session.AccountFor(JmapCapabilities.Submission) is { Length: > 0 } s ? s : Account;
    }

    public IAccountSession Session { get; }

    public IJmapClient Client => Session.Client;

    public string Account { get; }

    public string SubmissionAccount { get; }

    public string Username => Client.Session.Username;

    public static Result<ComposeContext> For(IAccountManager accounts, string accountKey)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var session = accounts.SessionFor(accountKey);
        return session is null ? Result<ComposeContext>.Fail(new ValidationError("The account is not signed in.")) : Result<ComposeContext>.Ok(new ComposeContext(session));
    }

    /// <summary>The Drafts and Sent folders; <paramref name="fresh"/> re-reads them (after a refusal that may mean the folder changed).</summary>
    public async Task<Result<MailboxRoles>> RolesAsync(bool fresh, CancellationToken ct)
    {
        var cache = RoleCache.GetValue(Client, static _ => new ConcurrentDictionary<string, MailboxRoles>(StringComparer.Ordinal));
        if (!fresh && cache.TryGetValue(Account, out var known))
        {
            return Result<MailboxRoles>.Ok(known);
        }

        var r = await ReaderCalls.CallAsync(Client, "Mailbox/get", new JsonObject { ["accountId"] = Account, ["ids"] = null, ["properties"] = new JsonArray("id", "role") }, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<MailboxRoles>.Fail(r.Error!);
        }

        var list = (r.Value!["list"] as JsonArray ?? []).OfType<JsonNode>().ToList();
        string? Id(string role) => list.FirstOrDefault(m => string.Equals(m.Str("role"), role, StringComparison.Ordinal)).Str("id");
        if (Id("drafts") is not { } drafts)
        {
            return Result<MailboxRoles>.Fail(new ValidationError("No Drafts folder."));
        }

        var roles = new MailboxRoles(drafts, Id("sent"), Id("inbox"));
        cache[Account] = roles;
        return Result<MailboxRoles>.Ok(roles);
    }
}
