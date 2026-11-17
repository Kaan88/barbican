// Mirrors: Rust chips.rs:370 prefetch; Bulwark stores/contact-store.ts getAutocomplete sources. Contract from the M3 plan section 4.5; owner composer-ui (M5 contacts adds the contact cards).

namespace Barbican.Core.Compose.Recipients;

/// <summary>Loads the autocomplete sources of an account: contacts and groups (M5; none before), recent recipients from Sent (M3).</summary>
public interface IRecipientSources
{
    Task<SuggestionSources> LoadAsync(string accountId, CancellationToken ct);
}
