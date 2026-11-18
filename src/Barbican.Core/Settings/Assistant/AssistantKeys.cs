// Mirrors: see IAssistantKeys. The in-memory default.

namespace Barbican.Core.Settings.Assistant;

/// <summary>The default <see cref="IAssistantKeys"/>: the key lives in memory until the process ends.</summary>
public sealed class AssistantKeys : IAssistantKeys
{
    private readonly Lock _lock = new();
    private string? _key;

    /// <inheritdoc/>
    public Task<string?> ReadAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult(_key);
        }
    }

    /// <inheritdoc/>
    public Task WriteAsync(string? key, CancellationToken ct)
    {
        lock (_lock)
        {
            _key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        }

        return Task.CompletedTask;
    }
}
