// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1027-1034 doContactGet and :178,2207 (contacts:read), lib/jmap/client.ts:8834-8853 getContact (the card, or null when there is none).
// Deviation: reads the store the Contacts view shows, so it also finds the cards of shared accounts that contact.search returns (the ids carry their account).
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Contacts;

internal sealed class PluginContactGet(IPluginContactsHost host) : PluginContactsMethod("contact.get", "contacts:read", host, false)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginContactsCall call, PluginValue arguments, CancellationToken ct)
    {
        var snapshot = await PluginContactsData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        if (!await call.Context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var b = new PluginValueBuilder();
        return Success(b, PluginContactsData.CardById(snapshot, arguments.Item(0).StringValue) is { } card ? PluginContactProjection.Card(b, card) : b.Null());
    }
}
