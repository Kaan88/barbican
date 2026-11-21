// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.filters.ts:46-60 (sieveClient: the server must have filters; the filters' own account) and lib/mailbox-shared-out.ts:13-42 isSharedOut
// (a folder, or one above it, that is shared with anyone; fails closed: an answer the server refuses counts as shared; false only when the server has no folder sharing at all),
// RFC 8620 section 5.3 (stateMismatch). Stalwart crates/jmap/src/sieve/{get,set}.rs, crates/jmap/src/mailbox/get.rs (shareWith).
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Reader;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Sieve;

namespace Barbican.Core.Plugins.Api.MailRules;

internal static class PluginRulesAccess
{
    /// <summary>The capability under which a server accepts <c>shareWith</c> on folders (client.ts:8530).</summary>
    internal const string ShareCapability = "urn:ietf:params:jmap:mail:share";

    /// <summary>The undo kind of a rule (c.filters.ts:230).</summary>
    internal const string UndoKind = "filters";

    internal static bool SieveAvailable(IAccountSession session) => session.Client.Session.Has(SieveScriptStore.Capability);

    /// <summary>A failed service call as an add-on error: a changed script or state is a conflict, everything else a plain failure (no server text reaches the add-on).</summary>
    internal static PluginCallOutcome Fail(JmapError? error) =>
        PluginCallOutcome.Failed(error is MethodError { Type: "stateMismatch" } or SetError { Type: "stateMismatch" } ? PluginErrorCode.Conflict : PluginErrorCode.Failed);

    /// <summary>True when the folder or one above it is shared with anyone, or the server cannot say (mailbox-shared-out.ts).</summary>
    internal static async Task<bool> IsSharedOutAsync(IJmapClient client, IReadOnlyList<JsonNode> mailboxes, string mailboxId, CancellationToken ct)
    {
        if (!client.Session.Has(ShareCapability)) return false;
        var parents = mailboxes.Where(m => m.Str("id") is not null).ToDictionary(m => m.Str("id")!, m => m.Str("parentId"), StringComparer.Ordinal);
        var chain = new List<string>();
        for (var id = mailboxId; id is not null && !chain.Contains(id, StringComparer.Ordinal); id = parents.GetValueOrDefault(id)) chain.Add(id);
        foreach (var id in chain)
        {
            var args = new JsonObject { ["accountId"] = client.Session.MailAccountId, ["ids"] = new JsonArray(id), ["properties"] = new JsonArray("id", "shareWith") };
            var got = await ReaderCalls.CallAsync(client, "Mailbox/get", args, ct).ConfigureAwait(false);
            if (!got.IsOk || (got.Value!["list"] as JsonArray)?.FirstOrDefault() is not JsonObject box) return true;
            if (box["shareWith"] is JsonObject { Count: > 0 }) return true;
        }

        return false;
    }
}
