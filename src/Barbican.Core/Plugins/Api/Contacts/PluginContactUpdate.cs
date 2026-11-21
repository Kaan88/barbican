// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1035-1041 doContactUpdate and :179,2208 (contacts:write), lib/jmap/client.ts:8900-8925 updateContact and lib/jmap/contact-wire.ts:62-105 contactToWire (the properties given replace
// the card's; null clears one; id, uid and addressBookIds are never cleared; resolves to undefined). Goes through the writer the Contacts view uses (IContactWriter.UpdateAsync: onBeforeContactUpdate, onAfterContactUpdate run).
// Only a card of the login's own account in books that allow writing (myRights.mayWrite when stated). Deviation: a uid or addressBookIds that differs from the card's is refused (Stalwart refuses a new uid; moving a card is not
// part of update here).
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Contacts;

internal sealed class PluginContactUpdate(IPluginContactsHost host) : PluginContactsMethod("contact.update", "contacts:write", host, true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && PluginContactInput.TryParse(PluginMailArgs.At(arguments, 1), false, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginContactsCall call, PluginValue arguments, CancellationToken ct)
    {
        var id = arguments.Item(0).StringValue;
        if (!PluginContactInput.TryParse(PluginMailArgs.At(arguments, 1), false, out var patch)) return Fail(PluginErrorCode.InvalidArguments);
        var snapshot = await PluginContactsData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null || PluginContactsData.CardById(snapshot, id) is not { } card) return Fail(PluginErrorCode.Unavailable);
        if (!PluginContactsData.CanChange(snapshot, card, false)) return Fail(PluginErrorCode.PermissionDenied);
        if (patch.TryGetPropertyValue("uid", out var uid))
        {
            if (!string.Equals(uid?.GetValue<string>(), card.Str("uid"), StringComparison.Ordinal)) return Fail(PluginErrorCode.InvalidArguments);
            _ = patch.Remove("uid");
        }

        if (patch.TryGetPropertyValue("addressBookIds", out var books))
        {
            var named = (books as JsonObject)?.Where(e => e.Value is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.True).Select(e => e.Key).Order(StringComparer.Ordinal).ToList() ?? [];
            if (!named.SequenceEqual(PluginContactsData.BookIdsOf(card).Order(StringComparer.Ordinal), StringComparer.Ordinal)) return Fail(PluginErrorCode.InvalidArguments);
            _ = patch.Remove("addressBookIds");
        }

        if (patch.Count == 0) return Done();
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var updated = await Host.Writer.UpdateAsync(call.AccountKey, id, patch, ct).ConfigureAwait(false);
        return updated.IsOk ? Done() : Fail(PluginErrorCode.Failed);
    }
}
