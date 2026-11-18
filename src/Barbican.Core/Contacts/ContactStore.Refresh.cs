// Mirrors: RFC 8620 5.2 (Foo/changes from a state, hasMoreChanges, cannotCalculateChanges), Rust mail.rs:254 load_mailboxes (a delta from the kept state, else a full read; the same rule as the calendar store), PORT-PLAN section 3.4, G-044;
// Stalwart crates/jmap/src/addressbook/changes.rs and contact/changes.rs. Owner con-data.

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
    public async Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        Held? held;
        lock (_lock)
        {
            _ = _held.TryGetValue(accountKey, out held);
        }

        if (held is null || _accounts.SessionFor(accountKey) is not { } session)
        {
            var loaded = await LoadAsync(accountKey, ct).ConfigureAwait(false);
            return loaded.IsOk ? Result<Unit>.Ok(default) : Result<Unit>.Fail(loaded.Error!);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await ApplyChangesAsync(accountKey, held, session.Client, ct).ConfigureAwait(false);

            // the server cannot say what changed from this state: read everything again (a fresh Held replaces this one)
            if (!result.IsOk && result.Error is MethodError { Type: "cannotCalculateChanges" })
            {
                var loaded = await LoadLockedAsync(accountKey, session.Client, ct).ConfigureAwait(false);
                return loaded.IsOk ? Result<Unit>.Ok(default) : Result<Unit>.Fail(loaded.Error!);
            }

            return result;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private async Task<Result<Unit>> ApplyChangesAsync(string key, Held held, IJmapClient client, CancellationToken ct)
    {
        var touched = new List<string>();
        var booksChanged = false;
        foreach (var account in held.States.Keys.ToList())
        {
            var one = await ApplyAccountAsync(key, held, client, account, ct).ConfigureAwait(false);
            if (!one.IsOk)
            {
                return Result<Unit>.Fail(one.Error!);
            }

            touched.AddRange(one.Value!.Touched);
            booksChanged |= one.Value.Books;
        }

        lock (_lock)
        {
            held.At = _time.GetUtcNow();
        }

        if (touched.Count > 0 || booksChanged)
        {
            Raise(new ContactChangedEventArgs(key, booksChanged, touched));
        }

        return Result<Unit>.Ok(default);
    }

    private async Task<Result<Applied>> ApplyAccountAsync(string key, Held held, IJmapClient client, string account, CancellationToken ct)
    {
        var (bookState, cardState) = held.States[account];
        if (bookState is null || cardState is null)
        {
            return Result<Applied>.Fail(new MethodError("AddressBook/changes", "cannotCalculateChanges", "no state kept"));
        }

        var books = await ChangesAsync(client, "AddressBook", account, bookState, ct).ConfigureAwait(false);
        if (!books.IsOk)
        {
            return Result<Applied>.Fail(books.Error!);
        }

        var cards = await ChangesAsync(client, "ContactCard", account, cardState, ct).ConfigureAwait(false);
        if (!cards.IsOk)
        {
            return Result<Applied>.Fail(cards.Error!);
        }

        var booksMoved = books.Value!.Changed.Count + books.Value.Destroyed.Count > 0;
        List<JsonNode>? list = null;
        var newBookState = books.Value.NewState;
        if (booksMoved)
        {
            var again = await BooksAsync(client, account, held.Primary, ct).ConfigureAwait(false);
            if (!again.IsOk)
            {
                return Result<Applied>.Fail(again.Error!);
            }

            list = again.Value!.Books;
            newBookState = again.Value.BookState ?? newBookState;
        }

        var fetched = await FetchChangedAsync(client, account, held.Primary, cards.Value!.Changed, ct).ConfigureAwait(false);
        if (!fetched.IsOk)
        {
            return Result<Applied>.Fail(fetched.Error!);
        }

        var gone = cards.Value.Destroyed.Select(i => ContactIds.Ns(held.Primary, account, i)).ToList();
        Merge(key, held, account, list, gone, fetched.Value!, (newBookState, cards.Value.NewState));
        return Result<Applied>.Ok(new Applied([.. gone, .. fetched.Value!.Select(c => c.Str("id") ?? string.Empty)], booksMoved));
    }

    /// <summary>Puts what /changes brought into the held data of one account: the new book list, removed and replaced or added cards, the new states.</summary>
    private void Merge(string key, Held held, string account, List<JsonNode>? books, List<string> gone, List<JsonNode> fetched, (string? Book, string? Card) states)
    {
        lock (_lock)
        {
            if (books is not null)
            {
                held.Books = [.. held.Books.Where(b => !string.Equals(b.Str("_acc"), account, StringComparison.Ordinal)), .. books];
            }

            var cards = held.Cards.Where(c => !gone.Contains(c.Str("id") ?? string.Empty, StringComparer.Ordinal)).ToList();
            foreach (var c in fetched)
            {
                var at = cards.FindIndex(x => string.Equals(x.Str("id"), c.Str("id"), StringComparison.Ordinal));
                if (at >= 0)
                {
                    cards[at] = c;
                }
                else
                {
                    cards.Add(c);
                }
            }

            held.States[account] = states;
            held.SetCards(key, cards);
        }
    }

    private static async Task<Result<List<JsonNode>>> FetchChangedAsync(IJmapClient client, string account, string primary, List<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return Result<List<JsonNode>>.Ok([]);
        }

        var got = await client.GetAllAsync("ContactCard/get", account, ids, null, ct).ConfigureAwait(false);
        if (!got.IsOk)
        {
            return Result<List<JsonNode>>.Fail(got.Error!);
        }

        List<JsonNode> cards = [.. got.Value!];
        foreach (var c in cards)
        {
            ContactIds.Namespace(c, primary, account);
        }

        return Result<List<JsonNode>>.Ok(cards);
    }

    /// <summary>The created, updated and destroyed ids since a state, all pages of it (hasMoreChanges).</summary>
    private static async Task<Result<ChangeSet>> ChangesAsync(IJmapClient client, string type, string account, string since, CancellationToken ct)
    {
        var changed = new List<string>();
        var destroyed = new List<string>();
        var state = since;
        while (true)
        {
            var args = new JsonObject { ["accountId"] = account, ["sinceState"] = state };
            var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call(type + "/changes", args, "h")), ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<ChangeSet>.Fail(r.Error!);
            }

            var res = r.Value!.MethodResponses[0][1];
            changed.AddRange(Ids(res.Arr("created")));
            changed.AddRange(Ids(res.Arr("updated")));
            destroyed.AddRange(Ids(res.Arr("destroyed")));
            state = res.Str("newState") ?? state;
            if (!res.True("hasMoreChanges"))
            {
                return Result<ChangeSet>.Ok(new ChangeSet([.. changed.Distinct(StringComparer.Ordinal)], destroyed, state));
            }
        }
    }

    private static IEnumerable<string> Ids(JsonArray? a) => (a ?? []).Select(x => x.AsString()).OfType<string>();

    private sealed record Applied(List<string> Touched, bool Books);

    private sealed record ChangeSet(List<string> Changed, List<string> Destroyed, string NewState);
}
