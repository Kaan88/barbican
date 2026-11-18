// Mirrors: Rust contacts.rs load (AddressBook/get, every card paged past the server's query cap and fetched in maxObjectsInGet chunks), Rust calendar.rs:273 load (the primary account first, shared accounts that fail are skipped). B-CON-001. Owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;

namespace Barbican.Core.Contacts;

public sealed partial class ContactStore
{
    /// <inheritdoc/>
    public async Task<Result<ContactSnapshot>> LoadAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        if (_accounts.SessionFor(accountKey) is not { } session)
        {
            return Result<ContactSnapshot>.Fail(new ValidationError("This account is not signed in."));
        }

        if (!session.Client.Session.Has(ContactOptions.Capability))
        {
            lock (_lock)
            {
                _ = _held.Remove(accountKey);
            }

            return Result<ContactSnapshot>.Ok(new ContactSnapshot(accountKey, [], [], null, null));
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LoadLockedAsync(accountKey, session.Client, ct).ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private async Task<Result<ContactSnapshot>> LoadLockedAsync(string key, IJmapClient client, CancellationToken ct)
    {
        var js = client.Session;
        var primary = js.AccountFor(ContactOptions.Capability);
        var held = new Held(primary, _time.GetUtcNow());
        var cards = new List<JsonNode>();
        foreach (var (account, _) in ContactJmap.Accounts(js, primary))
        {
            var one = await LoadAccountAsync(client, account, primary, ct).ConfigureAwait(false);
            if (!one.IsOk)
            {
                // a shared account that fails must not hide the user's own contacts
                if (string.Equals(account, primary, StringComparison.Ordinal))
                {
                    return Result<ContactSnapshot>.Fail(one.Error!);
                }

                continue;
            }

            held.Books.AddRange(one.Value!.Books);
            cards.AddRange(one.Value.Cards);
            held.States[account] = (one.Value.BookState, one.Value.CardState);
        }

        held.SetCards(key, cards);
        lock (_lock)
        {
            _held[key] = held;
        }

        Raise(new ContactChangedEventArgs(key, true, []));
        return Result<ContactSnapshot>.Ok(held.Snapshot);
    }

    /// <summary>The books and cards of one server account: AddressBook/get, then every card id (paged) and the cards in maxObjectsInGet chunks, never one back-referenced get.</summary>
    private static async Task<Result<AccountLoad>> LoadAccountAsync(IJmapClient client, string account, string primary, CancellationToken ct)
    {
        var books = await BooksAsync(client, account, primary, ct).ConfigureAwait(false);
        if (!books.IsOk)
        {
            return Result<AccountLoad>.Fail(books.Error!);
        }

        // the baseline for /changes is read before the cards, so a change made while they load is not missed
        var baseline = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("ContactCard/get", new JsonObject { ["accountId"] = account, ["ids"] = new JsonArray(), ["properties"] = new JsonArray("id") }, "s")), ct).ConfigureAwait(false);
        if (!baseline.IsOk)
        {
            return Result<AccountLoad>.Fail(baseline.Error!);
        }

        var state = baseline.Value!.MethodResponses[0][1].Str("state");
        var ids = await client.QueryAllAsync("ContactCard/query", account, [], ct).ConfigureAwait(false);
        if (!ids.IsOk)
        {
            return Result<AccountLoad>.Fail(ids.Error!);
        }

        var got = await client.GetAllAsync("ContactCard/get", account, ids.Value!, null, ct).ConfigureAwait(false);
        if (!got.IsOk)
        {
            return Result<AccountLoad>.Fail(got.Error!);
        }

        List<JsonNode> cards = [.. got.Value!];
        foreach (var c in cards)
        {
            ContactIds.Namespace(c, primary, account);
        }

        return Result<AccountLoad>.Ok(new AccountLoad(books.Value!.Books, cards, books.Value.BookState, state));
    }

    private static async Task<Result<AccountLoad>> BooksAsync(IJmapClient client, string account, string primary, CancellationToken ct)
    {
        var args = new JsonObject { ["accountId"] = account, ["ids"] = null };
        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("AddressBook/get", args, "a")), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<AccountLoad>.Fail(r.Error!);
        }

        var res = r.Value!.MethodResponses[0][1];
        var list = (res.Arr("list") ?? []).OfType<JsonObject>().OrderBy(b => b.Count("sortOrder") ?? 0).ThenBy(b => (b.Str("name") ?? string.Empty).ToLowerInvariant(), StringComparer.Ordinal).ToList();
        foreach (var book in list)
        {
            ContactIds.Namespace(book, primary, account);
        }

        return Result<AccountLoad>.Ok(new AccountLoad([.. list], [], res.Str("state"), null));
    }
}
