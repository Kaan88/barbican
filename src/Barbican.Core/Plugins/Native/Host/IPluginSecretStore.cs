// Mirrors: Rust ai.rs:25-65 (the API key lives in the OS credential store; B-AI-002). Core cannot see the platform credential store (Barbican.Platform.ISecretStore), so add-on secrets go through this seam;
// the app registers an adapter over ISecretStore. The default keeps secrets in memory, so a build without the adapter works but forgets them at exit.

namespace Barbican.Core.Plugins.Native.Host;

/// <summary>Where the secrets of all add-ons are kept, addressed by add-on id and name.</summary>
public interface IPluginSecretStore
{
    /// <summary>The stored secret, or null when there is none.</summary>
    Task<string?> ReadAsync(string pluginId, string name, CancellationToken ct);

    /// <summary>Stores the secret; null or empty removes it.</summary>
    Task WriteAsync(string pluginId, string name, string? value, CancellationToken ct);
}
