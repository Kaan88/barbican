// The import of a vCard file: the cards are created in chunks of 100 (or maxObjectsInSet when smaller), one ContactCard/set per chunk. A chunk the server refuses as a whole stops the import; a card it refuses is counted and the rest go on.
// Mirrors Rust contacts_x.rs:790 run_import (chunks of max_set.min(100), notCreated collected); Bulwark components/contacts/contact-import-dialog.tsx; lib/plugin-hook-catalog.ts:197 (onContactsImport). B-CON-016.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Contacts;

public sealed partial class ContactWriter
{
    /// <summary>Cards per ContactCard/set of an import.</summary>
    public const int ImportChunk = 100;

    public async Task<Result<int>> ImportAsync(string accountKey, string bookId, IReadOnlyList<JsonNode> cards, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(bookId);
        ArgumentNullException.ThrowIfNull(cards);
        var client = ContactJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<int>.Fail(client.Error!);
        }

        var (account, serverBook) = ServerBook(client.Value!, bookId);
        var created = 0;
        JmapError? refused = null;
        foreach (var chunk in cards.OfType<JsonObject>().Chunk(Math.Max(1, Math.Min(ImportChunk, client.Value!.Session.Limits.MaxObjectsInSet))))
        {
            var create = new JsonObject();
            for (var i = 0; i < chunk.Length; i++)
            {
                create["i" + i.ToString(CultureInfo.InvariantCulture)] = NewCard(chunk[i], serverBook);
            }

            var r = await ContactJmap.SetAsync(client.Value, "ContactCard", new JsonObject { ["accountId"] = account, ["create"] = create }, ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                await FinishImportAsync(accountKey, bookId, created, ct).ConfigureAwait(false);
                return Result<int>.FailPartial(r.Error!, created);
            }

            created += r.Value!.Created.Count;
            refused ??= ContactJmap.FirstFailure(r.Value);
        }

        await FinishImportAsync(accountKey, bookId, created, ct).ConfigureAwait(false);
        return refused is null ? Result<int>.Ok(created) : Result<int>.FailPartial(refused, created);
    }

    private async Task FinishImportAsync(string accountKey, string bookId, int created, CancellationToken ct)
    {
        if (created > 0)
        {
            hooks.Fire(HookNames.OnContactsImport, () => new ContactsImportPayload(bookId, created));
            _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        }
    }
}
