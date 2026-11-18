// Mirrors: Rust contacts.rs load (AddressBook/get, ContactCard/query + get_all in maxObjectsInGet chunks), contacts_x.rs:14 CAP; Rust calendar.rs:254 calendar_accounts (the same shape for the accounts that share their contacts), :1308 patch_local; PORT-PLAN section 3.4 (push types AddressBook and ContactCard through the generic /changes engine);
// RFC 8620 5.2 /changes and 5.5 paged queries, RFC 9610 (AddressBook, ContactCard); Bulwark stores/contacts-store.ts; Stalwart crates/jmap/src/contact/{get,query,changes}.rs, addressbook/get.rs (state strings are per type, ids paged by position, maxObjectsInGet is enforced). G-044 (contacts live). B-CON-001. Owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Sync;

namespace Barbican.Core.Contacts;

/// <summary>The default <see cref="IContactStore"/>: the address books and cards of every login, loaded in chunks, kept current by /changes after a push, patched optimistically by the writer.</summary>
public sealed partial class ContactStore : IContactStore, IDisposable
{
    private readonly IAccountManager _accounts;
    private readonly IHookBus _hooks;
    private readonly TimeProvider _time;
    private readonly ICalendarPush? _push;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Held> _held = new(StringComparer.Ordinal);
    private readonly List<Task> _refreshes = [];

    public ContactStore(IAccountManager accounts, IHookBus hooks, TimeProvider time, ICalendarPush? push = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(time);
        _accounts = accounts;
        _hooks = hooks;
        _time = time;
        _push = push;
        if (push is not null)
        {
            push.Changed += OnPush;
        }
    }

    /// <inheritdoc/>
    public event EventHandler<ContactChangedEventArgs>? Changed;

    /// <summary>When the data of a login was last read in full or by /changes (UTC); null when it is not loaded.</summary>
    public DateTimeOffset? LoadedAt(string accountKey)
    {
        lock (_lock)
        {
            return _held.TryGetValue(accountKey, out var h) ? h.At : null;
        }
    }

    /// <inheritdoc/>
    public ContactSnapshot? Snapshot(string accountKey)
    {
        lock (_lock)
        {
            return _held.TryGetValue(accountKey, out var h) ? h.Snapshot : null;
        }
    }

    /// <inheritdoc/>
    public JsonNode? Patch(string accountKey, string cardId, JsonObject patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        JsonNode? previous = null;
        lock (_lock)
        {
            if (_held.TryGetValue(accountKey, out var h) && h.Cards.FindIndex(c => string.Equals(c.Str("id"), cardId, StringComparison.Ordinal)) is var i and >= 0)
            {
                previous = h.Cards[i];

                // a copy, so a snapshot a view already holds does not change under it
                var changed = (JsonObject)previous.DeepClone();
                Barbican.Core.Calendar.JsonPointerPatch.Apply(changed, patch);
                var cards = new List<JsonNode>(h.Cards) { [i] = changed };
                h.SetCards(accountKey, cards);
            }
        }

        if (previous is not null)
        {
            Raise(new ContactChangedEventArgs(accountKey, false, [cardId]));
        }

        return previous;
    }

    /// <inheritdoc/>
    public void Restore(string accountKey, string cardId, JsonNode? previous)
    {
        if (previous is null)
        {
            return;
        }

        var found = false;
        lock (_lock)
        {
            if (_held.TryGetValue(accountKey, out var h) && h.Cards.FindIndex(c => string.Equals(c.Str("id"), cardId, StringComparison.Ordinal)) is var i and >= 0)
            {
                found = true;
                var cards = new List<JsonNode>(h.Cards) { [i] = previous.DeepClone() };
                h.SetCards(accountKey, cards);
            }
        }

        if (found)
        {
            Raise(new ContactChangedEventArgs(accountKey, false, [cardId]));
        }
    }

    /// <summary>Waits for the refreshes a push started (tests, shutdown).</summary>
    public async Task DrainAsync()
    {
        Task[] running;
        lock (_lock)
        {
            _ = _refreshes.RemoveAll(t => t.IsCompleted);
            running = [.. _refreshes];
        }

        await Task.WhenAll(running).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_push is not null)
        {
            _push.Changed -= OnPush;
        }

        _gate.Dispose();
    }

    private void Raise(ContactChangedEventArgs e) => Changed?.Invoke(this, e);

    private string? KeyOf(IJmapClient client) => _accounts.Accounts.Select(a => a.Key).FirstOrDefault(k => ReferenceEquals(_accounts.SessionFor(k)?.Client, client));

    // a push for a contact type: apply /changes to a login whose contacts are loaded (others load when the Contacts app opens)
    private void OnPush(object? sender, CalendarPushEventArgs e)
    {
        if (!e.Types.Contains("AddressBook", StringComparer.Ordinal) && !e.Types.Contains("ContactCard", StringComparer.Ordinal))
        {
            return;
        }

        if (KeyOf(e.Client) is not { } key || Snapshot(key) is null)
        {
            return;
        }

        var task = RefreshQuietlyAsync(key);
        lock (_lock)
        {
            _refreshes.Add(task);
        }
    }

#pragma warning disable CA1031 // a push-driven refresh has no caller to tell: the next push or the next open of the contacts loads again
    private async Task RefreshQuietlyAsync(string key)
    {
        try
        {
            _ = await RefreshAsync(key, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // see above
        }
    }
#pragma warning restore CA1031

    /// <summary>What one login's address books and cards are, and how to get at them again.</summary>
    private sealed class Held(string primary, DateTimeOffset at)
    {
        public string Primary { get; } = primary;

        public DateTimeOffset At { get; set; } = at;

        public List<JsonNode> Books { get; set; } = [];

        public List<JsonNode> Cards { get; private set; } = [];

        /// <summary>The state strings per server account: (AddressBook, ContactCard).</summary>
        public Dictionary<string, (string? Book, string? Card)> States { get; } = new(StringComparer.Ordinal);

        public ContactSnapshot Snapshot { get; private set; } = new(string.Empty, [], [], null, null);

        public void SetCards(string key, List<JsonNode> cards)
        {
            Cards = cards;
            Rebuild(key);
        }

        public void Rebuild(string key)
        {
            var (book, card) = States.TryGetValue(Primary, out var s) ? s : (null, null);
            Snapshot = new ContactSnapshot(key, [.. Books], [.. Cards], book, card);
        }
    }

    private sealed record AccountLoad(List<JsonNode> Books, List<JsonNode> Cards, string? BookState, string? CardState);
}
