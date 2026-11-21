// Mirrors: Rust ai.rs Config key storage; B-AI-002; ADR 0008 profile-scoped credentials.
// M6 integration: use the same secret entry as the assistant settings page.

using Barbican.Core.Settings.Assistant;

namespace Barbican.Platform;

/// <summary>The assistant API key in the current profile's credential store.</summary>
public sealed class SecretAssistantKeys(ISecretStore store) : IAssistantKeys
{
    private static readonly SecretName Entry = new("ai-api-key");

    public async Task<string?> ReadAsync(CancellationToken ct)
    {
        var result = await store.ReadAsync(Entry, ct).ConfigureAwait(false);
        return result.IsOk ? result.Value : null;
    }

    public async Task WriteAsync(string? key, CancellationToken ct)
    {
        var result = string.IsNullOrWhiteSpace(key)
            ? await store.DeleteAsync(Entry, ct).ConfigureAwait(false)
            : await store.WriteAsync(Entry, key.Trim(), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            // Never include provider or credential-store error text: it may contain a secret.
            throw new IOException("The assistant key could not be saved in the credential store.");
        }
    }
}
