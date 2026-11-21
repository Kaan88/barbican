// Mirrors: Rust ai.rs Config key storage; B-AI-002; ADR 0008 profile-scoped credentials; decision D-M6b-08.
// Add-on secrets in the current profile's credential store, one entry per add-on and name. The assistant's key keeps its entry from before add-ons existed (`ai-api-key`), so current users keep it.

using Barbican.Core.Plugins.Native.Host;

namespace Barbican.Platform;

/// <summary>The secrets of the native add-ons in the current profile's credential store.</summary>
public sealed class SecretPluginSecrets(ISecretStore store) : IPluginSecretStore
{
    // add-ons that stored a secret before add-ons existed: (id, name) -> the entry it already has
    private static readonly Dictionary<(string, string), SecretName> Legacy = new()
    {
        [("assistant", "api-key")] = new SecretName("ai-api-key"),
    };

    /// <inheritdoc/>
    public async Task<string?> ReadAsync(string pluginId, string name, CancellationToken ct)
    {
        var result = await store.ReadAsync(Entry(pluginId, name), ct).ConfigureAwait(false);
        return result.IsOk ? result.Value : null;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string pluginId, string name, string? value, CancellationToken ct)
    {
        var entry = Entry(pluginId, name);
        var result = string.IsNullOrWhiteSpace(value)
            ? await store.DeleteAsync(entry, ct).ConfigureAwait(false)
            : await store.WriteAsync(entry, value.Trim(), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            // Never include provider or credential-store error text: it may contain a secret.
            throw new IOException("The add-on secret could not be saved in the credential store.");
        }
    }

    private static SecretName Entry(string pluginId, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginId);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return Legacy.TryGetValue((pluginId, name), out var legacy) ? legacy : new SecretName("addon-secret", pluginId + "/" + name);
    }
}
