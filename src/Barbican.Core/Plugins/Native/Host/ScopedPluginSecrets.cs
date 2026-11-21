// Mirrors: decision D-M6b-08. An add-on sees only its own secrets: the id is fixed by the host, the add-on supplies only the name.

using Barbican.Plugins;

namespace Barbican.Core.Plugins.Native.Host;

/// <summary>The secrets of one add-on over the app's secret store.</summary>
public sealed class ScopedPluginSecrets(string pluginId, IPluginSecretStore store) : IPluginSecrets
{
    /// <inheritdoc/>
    public Task<string?> ReadAsync(string name, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return store.ReadAsync(pluginId, name, ct);
    }

    /// <inheritdoc/>
    public Task WriteAsync(string name, string? value, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return store.WriteAsync(pluginId, name, value, ct);
    }
}
