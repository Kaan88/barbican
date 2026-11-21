// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1081-1090 doAddressBookCreate and :186,2215 (contacts:write), lib/jmap/client.ts:7861-7880 createAddressBook (a new book in the login's own account; resolves to the book).
// Goes through the service the Contacts view uses (IAddressBooks.CreateAsync: AddressBook/set, the store learns the book). A blank name is refused (the service trims and refuses it), names over 255 characters too.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Contacts;

internal sealed class PluginAddressBookCreate(IPluginContactsHost host) : PluginContactsMethod("addressbook.create", "contacts:write", host, true)
{
    internal const int MaxNameChars = 255;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.TryString(PluginMailArgs.At(arguments, 0), MaxNameChars, out var name) && name.Trim().Length > 0 ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginContactsCall call, PluginValue arguments, CancellationToken ct)
    {
        var name = arguments.Item(0).StringValue.Trim();
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var made = await Host.Books.CreateAsync(call.AccountKey, name, ct).ConfigureAwait(false);
        if (!made.IsOk) return Fail(PluginErrorCode.Failed);
        var b = new PluginValueBuilder();
        var stored = Host.Store.Snapshot(call.AccountKey) is { } fresh ? PluginContactsData.BookById(fresh, made.Value!) : null;
        stored ??= new System.Text.Json.Nodes.JsonObject { ["id"] = made.Value, ["name"] = name };
        return Success(b, PluginContactProjection.Book(b, stored));
    }
}
