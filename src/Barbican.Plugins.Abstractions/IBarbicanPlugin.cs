// Mirrors: the Rust C ABI (barbican_plugin_manifest, barbican_plugin_call; docs/legacy/PLUGINS.md), dropped in favour of this interface (M2 plan 3.2, decision: two plugin
// systems over one catalogue); Bulwark lib/plugin-types.ts. Stub from stage F of M2; owner unified.

namespace Barbican.Plugins;

/// <summary>A native .NET plugin. The host calls handlers on the thread pool, with a time budget, and ignores a handler that throws or is late (R2-10).</summary>
public interface IBarbicanPlugin
{
    /// <summary>What the plugin declares.</summary>
    PluginManifest Manifest { get; }

    /// <summary>Called once after loading.</summary>
    Task InitializeAsync(PluginContext context, CancellationToken ct);

    /// <summary>The email-details hook: rows to show below the subject (none for "nothing to say").</summary>
    Task<IReadOnlyList<EmailDetailRow>> OnEmailDetailsAsync(EmailDetailsView email, CancellationToken ct);
}
