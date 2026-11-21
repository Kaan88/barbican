// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1053-1061 doContactList (an optional address book id; failures reported rather than read as an empty book) and :182,2211 (contacts:read), lib/jmap/client.ts:8776-8784 getContacts
// (the login's own account; all of it without a book). Deviation: the cards come from the store the Contacts view shows, and a list of more than 10 000 cards fails the call (Unavailable data is never cut short silently).
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Contacts;

internal sealed class PluginContactList(IPluginContactsHost host) : PluginContactsMethod("contact.list", "contacts:read", host, false)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.At(arguments, 0) is null or { Kind: PluginValueKind.Null } || PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginContactsCall call, PluginValue arguments, CancellationToken ct)
    {
        var bookId = PluginMailArgs.At(arguments, 0) is { Kind: PluginValueKind.String } id ? id.StringValue : null;
        var snapshot = await PluginContactsData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        if (!await call.Context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var cards = PluginContactsData.OwnCards(snapshot, bookId).Take(PluginContactsData.MaxListCards + 1).ToList();
        if (cards.Count > PluginContactsData.MaxListCards) return Fail(PluginErrorCode.Failed);
        var b = new PluginValueBuilder();
        return Success(b, b.Array([.. cards.Select(c => PluginContactProjection.Card(b, c))]));
    }
}
