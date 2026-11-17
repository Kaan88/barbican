// Mirrors: Rust jmap.rs:831 identities, compose.rs:290 apply_identities (textSignature, htmlSignature); Bulwark lib/jmap/client.ts getIdentities; RFC 8621 section 6 (Identity/get on the submission account).

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
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

        var r = await ReaderCalls.CallAsync(ctx.Value!.Client, "Identity/get", new JsonObject { ["accountId"] = ctx.Value.SubmissionAccount, ["ids"] = null }, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<IReadOnlyList<ComposeIdentity>>.Fail(r.Error!);
        }

        IReadOnlyList<ComposeIdentity> list =
        [
            .. (r.Value!["list"] as JsonArray ?? []).OfType<JsonNode>()
                .Select(n => (Node: n, Identity: ModelReader.Identity(n)))
                .Where(x => x.Identity is not null)
                .Select(x => new ComposeIdentity(x.Identity!, x.Node.Str("textSignature") ?? "", x.Node.Str("htmlSignature") ?? "")),
        ];
        return Result<IReadOnlyList<ComposeIdentity>>.Ok(list);
    }
}
