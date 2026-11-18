// Mirrors: Rust contacts.rs (create, save, delete, move, groups: commits e75e107, 4d24260), contacts_x.rs:790 run_import, :215 save (create with uid and addressBookIds, update); Bulwark stores/contacts-store.ts (createContact, updateContact, deleteContact, moveContact, group members), lib/contacts/merge.ts;
// RFC 9610 ContactCard/set; Stalwart crates/jmap/src/contact/set.rs (a keywords/x pointer patch on a card without keywords is refused: categories send the whole keywords map, B-CON-006); lib/plugin-hook-catalog.ts:191-201. B-CON-002..006, B-CON-016, B-CON-024. Owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Contacts;

/// <summary>The default <see cref="IContactWriter"/>: every contact write goes through here, with the plugin hooks, the optimistic change of the <see cref="IContactStore"/> and its rollback, then a /changes refresh.</summary>
public sealed partial class ContactWriter(IAccountManager accounts, IContactStore store, IAddressBooks books, IHookBus hooks) : IContactWriter
{
    /// <summary>The write the user asked for was stopped by a plugin (an intercept hook said no).</summary>
    internal static readonly ValidationError StoppedByPlugin = new("A plugin stopped this change.");

    public async Task<Result<string>> CreateAsync(string accountKey, string? bookId, JsonObject card, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(card);
        var client = ContactJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<string>.Fail(client.Error!);
        }

        var target = await BookAsync(client.Value!, accountKey, bookId, ct).ConfigureAwait(false);
        if (!target.IsOk)
        {
            return Result<string>.Fail(target.Error!);
        }

        var (account, serverBook) = target.Value;
        var data = NewCard(card, serverBook);
        if (!await hooks.AllowAsync(HookNames.OnBeforeContactCreate, () => new ContactCardPayload(null, data.DeepClone()), ct).ConfigureAwait(false))
        {
            return Result<string>.Fail(StoppedByPlugin);
        }

        var created = await ContactJmap.CreateAsync(client.Value!, "ContactCard", account, data, ct).ConfigureAwait(false);
        if (!created.IsOk)
        {
            return created;
        }

        var primary = client.Value!.Session.AccountFor(ContactOptions.Capability);
        var id = ContactIds.Ns(primary, account, created.Value!);
        hooks.Fire(HookNames.OnAfterContactCreate, () => new ContactCardPayload(id, data.DeepClone()));
        _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        return Result<string>.Ok(id);
    }

    public async Task<Result<Unit>> UpdateAsync(string accountKey, string cardId, JsonObject patch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(cardId);
        ArgumentNullException.ThrowIfNull(patch);
        var client = ContactJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        if (!await hooks.AllowAsync(HookNames.OnBeforeContactUpdate, () => new ContactUpdatePayload(cardId, patch.DeepClone()), ct).ConfigureAwait(false))
        {
            return Result<Unit>.Fail(StoppedByPlugin);
        }

        var done = await WritePatchAsync(client.Value!, accountKey, cardId, patch, ct).ConfigureAwait(false);
        if (done.IsOk)
        {
            hooks.Fire(HookNames.OnAfterContactUpdate, () => new ContactCardPayload(cardId, StoredCard(accountKey, cardId)?.DeepClone() ?? patch.DeepClone()));
        }

        return done;
    }

    public async Task<Result<Unit>> DeleteAsync(string accountKey, IReadOnlyList<string> cardIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(cardIds);
        var client = ContactJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        if (cardIds.Count == 0)
        {
            return Result<Unit>.Ok(default);
        }

        var groupIds = cardIds.Where(i => string.Equals(StoredCard(accountKey, i).Str("kind"), "group", StringComparison.Ordinal)).ToList();
        if (!await hooks.AllowAsync(HookNames.OnBeforeContactDelete, () => new ContactDeletePayload([.. cardIds]), ct).ConfigureAwait(false))
        {
            return Result<Unit>.Fail(StoppedByPlugin);
        }

        foreach (var perAccount in cardIds.Select(i => (Ref: ServerRef(client.Value!, accountKey, i), Id: i)).GroupBy(x => x.Ref.Account, StringComparer.Ordinal))
        {
            var done = await ContactJmap.DestroyManyAsync(client.Value!, "ContactCard", perAccount.Key, [.. perAccount.Select(x => x.Ref.ServerId)], null, ct).ConfigureAwait(false);
            if (!done.IsOk)
            {
                _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
                return done;
            }
        }

        hooks.Fire(HookNames.OnAfterContactDelete, () => new ContactDeletePayload([.. cardIds]));
        foreach (var g in groupIds)
        {
            hooks.Fire(HookNames.OnContactGroupChange, () => new ContactGroupChangePayload(g, "deleted"));
        }

        _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> cardIds, string bookId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(cardIds);
        ArgumentNullException.ThrowIfNull(bookId);
        var client = ContactJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        var (bookAccount, serverBook) = ServerBook(client.Value!, bookId);
        foreach (var id in cardIds)
        {
            // a card cannot change accounts: the book is in the card's own account
            if (!string.Equals(ServerRef(client.Value!, accountKey, id).Account, bookAccount, StringComparison.Ordinal))
            {
                return Result<Unit>.Fail(new ValidationError("A contact cannot be moved to an address book of another account."));
            }
        }

        foreach (var id in cardIds)
        {
            var patch = new JsonObject { ["addressBookIds"] = new JsonObject { [serverBook] = true } };
            var done = await WritePatchAsync(client.Value!, accountKey, id, patch, ct, refresh: false).ConfigureAwait(false);
            if (!done.IsOk)
            {
                _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
                return done;
            }

            hooks.Fire(HookNames.OnContactMove, () => new ContactMovePayload(id, bookId));
        }

        _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        return Result<Unit>.Ok(default);
    }
}
