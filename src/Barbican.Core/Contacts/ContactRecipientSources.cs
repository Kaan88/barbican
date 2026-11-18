// Mirrors: Rust chips.rs prefetch :370 (contacts and groups join the recent recipients), contacts.rs:140 is_member (members keyed by uid, bare or urn:uuid:, or by id), :399 people; Bulwark stores/contact-store.ts getAutocomplete (contacts, groups, recents); D-M5-06 (the composer keeps its M3 seam and contacts decorate it). Owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Contacts;

/// <summary>The composer's autocomplete sources with the contacts and groups of the login added to what the inner source (the people written to, from Sent) gives.</summary>
public sealed class ContactRecipientSources(IRecipientSources inner, IContactStore store, IAccountManager accounts) : IRecipientSources
{
    public async Task<SuggestionSources> LoadAsync(string accountId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        var sources = await inner.LoadAsync(accountId, ct).ConfigureAwait(false);
        if (accounts.SessionFor(accountId)?.Client.Session.Has(ContactOptions.Capability) != true)
        {
            return sources;
        }

        var snapshot = store.Snapshot(accountId);
        if (snapshot is null)
        {
            var loaded = await store.LoadAsync(accountId, ct).ConfigureAwait(false);
            snapshot = loaded.IsOk ? loaded.Value : null;
        }

        return snapshot is null ? sources : sources with { Contacts = ContactPairs(snapshot), Groups = Groups(snapshot) };
    }

    private static List<(string Name, string Email)> ContactPairs(ContactSnapshot s)
    {
        var pairs = new List<(string, string)>();
        foreach (var card in s.Cards.Where(c => !string.Equals(c.Str("kind"), "group", StringComparison.Ordinal)))
        {
            var name = ContactLookup.DisplayName(card);
            pairs.AddRange(ContactJson.Values(card, "emails").Select(e => (e.Str("address") ?? string.Empty).Trim()).Where(e => e.Length > 0).Select(e => (name, e)));
        }

        return pairs;
    }

    private static List<(string Id, string Name, IReadOnlyList<Recipient> Members)> Groups(ContactSnapshot s)
    {
        var groups = new List<(string, string, IReadOnlyList<Recipient>)>();
        var people = s.Cards.Where(c => !string.Equals(c.Str("kind"), "group", StringComparison.Ordinal)).ToList();
        foreach (var group in s.Cards.Where(c => string.Equals(c.Str("kind"), "group", StringComparison.Ordinal)))
        {
            var members = new List<Recipient>();
            foreach (var card in people.Where(p => IsMember(group, p)))
            {
                // the preferred address first, else the first one
                var emails = ContactJson.Values(card, "emails").OrderBy(e => ContactJson.Int(ContactJson.Get(e, "pref")) ?? long.MaxValue).Select(e => (e.Str("address") ?? string.Empty).Trim()).Where(e => e.Length > 0).ToList();
                if (emails.Count > 0)
                {
                    members.Add(Recipient.Addr(ContactLookup.DisplayName(card), emails[0]));
                }
            }

            groups.Add((group.Str("id") ?? string.Empty, ContactLookup.DisplayName(group), members));
        }

        return groups;
    }

    /// <summary>Whether <paramref name="card"/> is a member of <paramref name="group"/>: members are keyed by uid (bare or "urn:uuid:") or by id.</summary>
    internal static bool IsMember(JsonNode group, JsonNode card)
    {
        if (ContactJson.Get(group, "members") is not JsonObject members)
        {
            return false;
        }

        var keys = ContactMerge.MemberKeys(card);
        return members.Any(kv => ContactJson.IsTrue(kv.Value) && keys.Contains(kv.Key, StringComparer.Ordinal));
    }
}
