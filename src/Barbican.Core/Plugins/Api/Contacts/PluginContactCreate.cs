// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1043-1051 doContactCreate and :180,2209 (contacts:write), lib/jmap/client.ts:8855-8898 createContact (a card in the login's own account; the book is the one named, else the
// default or first book; a uid is added; resolves to the new card). Goes through the writer the Contacts view uses (IContactWriter.CreateAsync: onBeforeContactCreate, onAfterContactCreate run, the store learns the card).
// The book must be the login's own and its rights must allow writing (myRights.mayWrite when the book states it). Deviation: only one address book can be named (the writer creates a card in one book), and a card another
// add-on's hook stopped fails with Failed.
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Contacts;

internal sealed class PluginContactCreate(IPluginContactsHost host) : PluginContactsMethod("contact.create", "contacts:write", host, true,
    argumentLimits: PluginContactInput.Limits)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginContactInput.TryParse(PluginMailArgs.At(arguments, 0), true, out var card) && BookOf(card, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginContactsCall call, PluginValue arguments, CancellationToken ct)
    {
        if (!PluginContactInput.TryParse(PluginMailArgs.At(arguments, 0), true, out var card) || !BookOf(card, out var requested)) return Fail(PluginErrorCode.InvalidArguments);
        _ = card.Remove("addressBookIds");
        var snapshot = await PluginContactsData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        var book = PluginContactsData.PickBook(snapshot, requested);
        if (requested is not null && book is null) return Fail(PluginErrorCode.Unavailable);
        if (book is not null && !PluginContactsData.CanWriteBook(book)) return Fail(PluginErrorCode.PermissionDenied);
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var created = await Host.Writer.CreateAsync(call.AccountKey, book?.Str("id"), card, ct).ConfigureAwait(false);
        if (!created.IsOk) return Fail(PluginErrorCode.Failed);
        var b = new PluginValueBuilder();
        var stored = Host.Store.Snapshot(call.AccountKey) is { } fresh ? PluginContactsData.CardById(fresh, created.Value!) : null;
        if (stored is null)
        {
            card["id"] = created.Value;
            stored = card;
        }

        return Success(b, PluginContactProjection.Card(b, stored));
    }

    /// <summary>The one book a card names (none: the default); false when it names several.</summary>
    private static bool BookOf(JsonObject card, out string? bookId)
    {
        bookId = null;
        if (card["addressBookIds"] is not JsonObject ids) return true;
        var named = ids.Where(e => e.Value is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.True).Select(e => e.Key).ToList();
        if (named.Count > 1) return false;
        bookId = named.Count == 1 && named[0].Length is > 0 and <= PluginMailArgs.MaxIdLength ? named[0] : null;
        return named.Count == 0 || bookId is not null;
    }
}
