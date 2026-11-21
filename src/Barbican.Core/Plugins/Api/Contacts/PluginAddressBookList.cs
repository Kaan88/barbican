// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1070-1080 doAddressBookList and :185,2214 (contacts:read): the books of the login's own account (client.getAddressBooks), failures reported rather than read as "no books"
// (#730). Deviation: the books come from the store the Contacts view shows; shareWith (other people's principal ids) is not part of the answer.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Contacts;

internal sealed class PluginAddressBookList(IPluginContactsHost host) : PluginContactsMethod("addressbook.list", "contacts:read", host, false)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override async Task<PluginCallOutcome> RunAsync(PluginContactsCall call, PluginValue arguments, CancellationToken ct)
    {
        var snapshot = await PluginContactsData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        if (!await call.Context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var b = new PluginValueBuilder();
        return Success(b, b.Array([.. PluginContactsData.OwnBooks(snapshot).Select(book => PluginContactProjection.Book(b, book))]));
    }
}
