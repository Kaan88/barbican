// Mirrors: Rust contacts.rs (suggestions from the contact list), chips.rs prefetch; Bulwark components/email/recipient-popover.tsx, address-context-menu.tsx (View contact, Add to contacts), stores/contact-store.ts getAutocomplete, lib/plugin-hook-catalog.ts:202 (onProvideRecipientSuggestions); I-192, I-193, I-204, I-205, I-231, I-247. B-CON-002.
// The seam between contacts and mail: the reader's contact card and the address menu read this and never read IContactStore. Owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Contacts;

/// <summary>The default <see cref="IContactLookup"/>: every signed-in login's contacts, loaded on first use.</summary>
public sealed class ContactLookup(IContactStore store, IAccountManager accounts, IHookBus hooks) : IContactLookup, IDisposable
{
    private readonly SemaphoreSlim _loading = new(1, 1);

    public void Dispose() => _loading.Dispose();

    /// <summary>The name a contact is listed under: its full name, else its name parts, else its first address (Rust full_name).</summary>
    public static string DisplayName(JsonNode card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (ContactJson.Get(card, "name").Str("full") is { Length: > 0 } full)
        {
            return full;
        }

        var parts = ContactJson.Get(ContactJson.Get(card, "name"), "components") is JsonArray a ? a.Select(x => x.Str("value")).OfType<string>().ToList() : [];
        if (parts.Count > 0)
        {
            return string.Join(' ', parts);
        }

        return ContactJson.Values(card, "emails").Select(e => e.Str("address")).FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "(no name)";
    }

    public async Task<IReadOnlyList<ContactMatch>> SuggestAsync(string text, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        var needle = ContactText.Fold(text.Trim());
        if (needle.Length == 0 || max <= 0)
        {
            return [];
        }

        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        var hits = new List<(int Rank, string Name, ContactMatch Match)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in accounts.Accounts.Select(a => a.Key))
        {
            foreach (var card in store.Snapshot(key)?.Cards ?? [])
            {
                if (string.Equals(card.Str("kind"), "group", StringComparison.Ordinal))
                {
                    continue;
                }

                var name = DisplayName(card);
                foreach (var email in ContactJson.Values(card, "emails").Select(e => (e.Str("address") ?? string.Empty).Trim()).Where(e => e.Length > 0))
                {
                    if (Rank(needle, ContactText.Fold(name), ContactText.Fold(email)) is { } rank && seen.Add(email))
                    {
                        hits.Add((rank, name, new ContactMatch(key, card, email)));
                    }
                }
            }
        }

        var best = hits.OrderBy(h => h.Rank).ThenBy(h => ContactText.Fold(h.Name), StringComparer.Ordinal).ThenBy(h => h.Match.Email, StringComparer.OrdinalIgnoreCase).Select(h => h.Match).Take(max).ToList();
        return await ShapeAsync(text, max, best, ct).ConfigureAwait(false);
    }

    public async Task<ContactMatch?> FindByEmailAsync(string address, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(address);
        var wanted = address.Trim();
        if (wanted.Length == 0)
        {
            return null;
        }

        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        foreach (var key in accounts.Accounts.Select(a => a.Key))
        {
            foreach (var card in store.Snapshot(key)?.Cards ?? [])
            {
                if (string.Equals(card.Str("kind"), "group", StringComparison.Ordinal))
                {
                    continue;
                }

                var email = ContactJson.Values(card, "emails").Select(e => (e.Str("address") ?? string.Empty).Trim()).FirstOrDefault(e => string.Equals(e, wanted, StringComparison.OrdinalIgnoreCase));
                if (email is not null)
                {
                    return new ContactMatch(key, card, email);
                }
            }
        }

        return null;
    }

    /// <summary>0: the name starts with the text; 1: the address does; 2: either contains it; null: no match.</summary>
    private static int? Rank(string needle, string name, string email) =>
        name.StartsWith(needle, StringComparison.Ordinal) ? 0 : email.StartsWith(needle, StringComparison.Ordinal) ? 1 : name.Contains(needle, StringComparison.Ordinal) || email.Contains(needle, StringComparison.Ordinal) ? 2 : null;

    /// <summary>The suggestions as plugins may add or change them (onProvideRecipientSuggestions, a transform); an entry a plugin adds has no card, so it gets a minimal one.</summary>
    private async Task<IReadOnlyList<ContactMatch>> ShapeAsync(string query, int max, List<ContactMatch> best, CancellationToken ct)
    {
        if (!hooks.HasHandlers(HookNames.OnProvideRecipientSuggestions))
        {
            return best;
        }

        var shaped = await hooks.ShapeAsync(HookNames.OnProvideRecipientSuggestions, new ContactSuggestionsPayload(query, [.. best.Select(m => new ContactSuggestion(DisplayName(m.Card), m.Email))]), ct).ConfigureAwait(false);
        var output = new List<ContactMatch>();
        foreach (var s in shaped.Suggestions)
        {
            var known = best.FirstOrDefault(m => string.Equals(m.Email, s.Email, StringComparison.OrdinalIgnoreCase));
            output.Add(known ?? new ContactMatch(string.Empty, new JsonObject { ["name"] = new JsonObject { ["full"] = s.Name }, ["emails"] = new JsonObject { ["e0"] = new JsonObject { ["address"] = s.Email } } }, s.Email));
        }

        return [.. output.Take(max)];
    }

    /// <summary>Loads the contacts of every login that has them and has not been read yet.</summary>
    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        await _loading.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var key in accounts.Accounts.Select(a => a.Key))
            {
                if (store.Snapshot(key) is null && accounts.SessionFor(key)?.Client.Session.Has(ContactOptions.Capability) == true)
                {
                    _ = await store.LoadAsync(key, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _ = _loading.Release();
        }
    }
}
