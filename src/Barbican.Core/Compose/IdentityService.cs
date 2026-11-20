// Mirrors: Rust jmap.rs:831 identities, compose.rs:290 apply_identities (textSignature, htmlSignature); Bulwark lib/jmap/client.ts getIdentities; RFC 8621 section 6 (Identity/get on the submission account).

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Reader;

namespace Barbican.Core.Compose;

/// <summary>The sender identities of an account with their signatures.</summary>
public sealed class IdentityService(IAccountManager accounts) : IIdentityService
{
    public async Task<Result<IReadOnlyList<ComposeIdentity>>> ListAsync(string accountId, CancellationToken ct)
    {
        var ctx = ComposeContext.For(accounts, accountId);
        if (!ctx.IsOk)
        {
            return Result<IReadOnlyList<ComposeIdentity>>.Fail(ctx.Error!);
        }

        return await ListCapturedAsync(ctx.Value!.Client, ctx.Value.SubmissionAccount, ct).ConfigureAwait(false);
    }

    internal static async Task<Result<IReadOnlyList<ComposeIdentity>>> ListCapturedAsync(IJmapClient client, string jmapAccountId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(jmapAccountId);
        var r = await ReaderCalls.CallAsync(client, "Identity/get", new JsonObject { ["accountId"] = jmapAccountId, ["ids"] = null }, ct).ConfigureAwait(false);
        if (!r.IsOk) return Result<IReadOnlyList<ComposeIdentity>>.Fail(r.Error!);
        if (r.Value is not { } body || !string.Equals(body.Str("accountId"), jmapAccountId, StringComparison.Ordinal) ||
            body["list"] is not JsonArray rows)
            return Result<IReadOnlyList<ComposeIdentity>>.Fail(new ValidationError("The identity response did not match its account."));

        IReadOnlyList<ComposeIdentity> list =
        [
            .. rows.OfType<JsonNode>()
                .Select(n => (Node: n, Identity: ModelReader.Identity(n)))
                .Where(x => x.Identity is not null)
                .Select(x => new ComposeIdentity(x.Identity!, x.Node.Str("textSignature") ?? "", x.Node.Str("htmlSignature") ?? "")),
        ];
        return Result<IReadOnlyList<ComposeIdentity>>.Ok(list);
    }
}
