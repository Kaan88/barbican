// Mirrors: see IRecipientSources. Default registration from the M3 plan section 4.5; owner composer-ui replaces it with the Sent-based source.

namespace Barbican.Core.Compose.Recipients;

/// <summary>The default: no suggestions.</summary>
public sealed class EmptyRecipientSources : IRecipientSources
{
    public Task<SuggestionSources> LoadAsync(string accountId, CancellationToken ct) => Task.FromResult(new SuggestionSources());
}
