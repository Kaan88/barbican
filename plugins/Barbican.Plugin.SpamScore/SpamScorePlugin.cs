// Mirrors: tests/fixtures/plugins/spam-score/src/{lib.rs,spam.rs} (the Rust source stays the reference for the port, D-234); Bulwark's spam-score plugin
// (X-Spam-Status, X-Spam-Score, X-Spam-Flag headers). Stub from stage F of M2: the manifest is real, the handler returns no rows until stream unified ports spam.rs.

using Barbican.Plugins;

namespace Barbican.Plugin.SpamScore;

/// <summary>Shows the spam analysis of a message from its spam headers.</summary>
public sealed class SpamScorePlugin : IBarbicanPlugin
{
    /// <inheritdoc />
    public PluginManifest Manifest { get; } = new(1, "spam-score", "Spam Score", "0.1.0", "Shows the spam analysis of a message from its spam headers.", ["email.details"]);

    /// <inheritdoc />
    public Task InitializeAsync(PluginContext context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailDetailRow>> OnEmailDetailsAsync(EmailDetailsView email, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<EmailDetailRow>>([]);
}
