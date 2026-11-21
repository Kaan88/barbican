// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1063-1068 doContactDelete and :183,2212 (contacts:write), lib/jmap/client.ts:8927-8948 deleteContact (destroys the card; resolves to undefined; an id the server does not know fails).
// Goes through the writer the Contacts view uses (IContactWriter.DeleteAsync: onBeforeContactDelete, onAfterContactDelete run). Only a card of the login's own account in books that allow writing and deleting
// (myRights.mayWrite and mayDelete when stated).
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Contacts;

internal sealed class PluginContactDelete(IPluginContactsHost host) : PluginContactsMethod("contact.delete", "contacts:write", host, true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginContactsCall call, PluginValue arguments, CancellationToken ct)
    {
        var id = arguments.Item(0).StringValue;
        var snapshot = await PluginContactsData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null || PluginContactsData.CardById(snapshot, id) is not { } card) return Fail(PluginErrorCode.Unavailable);
        if (!PluginContactsData.CanChange(snapshot, card, true)) return Fail(PluginErrorCode.PermissionDenied);
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var deleted = await Host.Writer.DeleteAsync(call.AccountKey, [id], ct).ConfigureAwait(false);
        return deleted.IsOk ? Done() : Fail(PluginErrorCode.Failed);
    }
}
