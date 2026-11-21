// Mirrors: see IPluginSecretStore. The in-memory default.

namespace Barbican.Core.Plugins.Native.Host;

/// <summary>The default <see cref="IPluginSecretStore"/>: secrets live in memory until the process ends.</summary>
public sealed class PluginSecretMemory : IPluginSecretStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(string, string), string> _values = [];

    /// <inheritdoc/>
    public Task<string?> ReadAsync(string pluginId, string name, CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult(_values.TryGetValue((pluginId, name), out var value) ? value : null);
        }
    }

    /// <inheritdoc/>
    public Task WriteAsync(string pluginId, string name, string? value, CancellationToken ct)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _ = _values.Remove((pluginId, name));
            }
            else
            {
                _values[(pluginId, name)] = value.Trim();
            }
        }

        return Task.CompletedTask;
    }
}
