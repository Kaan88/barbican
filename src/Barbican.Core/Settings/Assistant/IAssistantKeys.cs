// Mirrors: Rust ai.rs:25-65 (the API key lives in the OS credential store, not in settings.json; B-AI-002). Core cannot see the platform credential store (Barbican.Platform.ISecretStore), so the key goes through this seam; the app registers an adapter over ISecretStore under the name `ai-api-key`
// (a request to the integrator, M6 Fable log). The default keeps the key in memory for the session, so a build without the adapter works but forgets the key at exit. Owner set-data.

namespace Barbican.Core.Settings.Assistant;

/// <summary>Where the assistant's API key is kept. The key is never logged and never written to a profile file.</summary>
public interface IAssistantKeys
{
    /// <summary>The stored key, or null when there is none.</summary>
    Task<string?> ReadAsync(CancellationToken ct);

    /// <summary>Stores the key; null or empty removes it.</summary>
    Task WriteAsync(string? key, CancellationToken ct);
}
