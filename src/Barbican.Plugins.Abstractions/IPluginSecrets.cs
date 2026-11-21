// Mirrors: Rust ai.rs:25-65 (the API key lives in the OS credential store; B-AI-002), generalized for add-ons (decision D-M6b-08).

namespace Barbican.Plugins;

/// <summary>Secrets of this add-on in the OS credential store. The host scopes the names to the add-on, so one add-on cannot read another's. Never log a value.</summary>
public interface IPluginSecrets
{
    /// <summary>The stored secret, or null when there is none.</summary>
    Task<string?> ReadAsync(string name, CancellationToken ct);

    /// <summary>Stores the secret; null or empty removes it.</summary>
    Task WriteAsync(string name, string? value, CancellationToken ct);
}
