// Mirrors: Bulwark lib/plugin-sandbox/host-api-mail.ts:73-97 doMailboxesList / mailboxToView and host-api.ts:337,2446.
// Deviation: Bulwark's list also holds the shared folders the user has open (namespaced ids). Barbican lists the signed-in login's own mailbox
// tree only; a shared account's folders are not enumerable here, so email.list / email.search cannot name one (see M6b-api-inventory.md).
using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Mail;

internal sealed class PluginMailboxesList(IPluginMailReaders readers) : PluginMailMethod("mailboxes.list", readers)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, IAccountSession session, PluginValue arguments, CancellationToken ct)
    {
        var tree = await Readers.Reader(session, null).GetMailboxesAsync(ct).ConfigureAwait(false);
        if (!tree.IsOk) return Failure(tree.Error);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        var b = new PluginValueBuilder();
        return Success(b, b.Array([.. tree.Value!.All.Select(m => PluginMailProjection.Mailbox(b, m))]));
    }
}
