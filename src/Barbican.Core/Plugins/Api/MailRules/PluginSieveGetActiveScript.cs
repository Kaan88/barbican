// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:303,1957-1966,2407 doSieveGetActiveScript (filters:read): { id, name, content } of the account's active Sieve script, or null when the server has no filters or no script is
// active. Read through SieveScriptStore (RFC 9661 SieveScript/get and the blob download; Stalwart crates/jmap/src/sieve/get.rs). The script is the user's own and may hold anything the user wrote: it is returned as text only.
using System.Text;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Sieve;

namespace Barbican.Core.Plugins.Api.MailRules;

internal sealed class PluginSieveGetActiveScript() : PluginScopedMethod("sieve.getActiveScript", "filters:read")
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var client = capture!.Session.Client;
        var b = new PluginValueBuilder();
        if (!PluginRulesAccess.SieveAvailable(capture.Session)) return Success(b, b.Null());
        var read = await new SieveScriptStore(client).ReadAsync(ct).ConfigureAwait(false);
        if (!read.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        var active = read.Value!.Scripts.FirstOrDefault(s => s.IsActive);
        if (active is null) return Success(b, b.Null());
        string content;
        if (string.Equals(read.Value.Target?.Id, active.Id, StringComparison.Ordinal))
        {
            content = read.Value.Text;
        }
        else
        {
            var blob = await client.DownloadAsync(client.Session.AccountFor(SieveScriptStore.Capability), active.BlobId, "script.sieve", "application/sieve", ct).ConfigureAwait(false);
            if (!blob.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
            using var stream = blob.Value!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            content = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }

        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        return Success(b, b.Object(("id", b.String(active.Id)), ("name", b.String(active.Name)), ("content", b.String(content))));
    }
}
