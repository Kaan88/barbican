// Mirrors: tests/fixtures/plugins/spam-score/src/lib.rs (the manifest: api 1, id spam-score, the email.details hook; the row: label "Spam analysis", value
// "Spam  ·  detail", tone danger / warning / success) and spam.rs (the analysis, in SpamAnalyzer); D-234: a native .NET plugin replaces the C ABI DLL;
// decision M2-Q3: bundled and enabled. Owner unified (M2 wave 2).

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
    public Task<IReadOnlyList<EmailDetailRow>> OnEmailDetailsAsync(EmailDetailsView email, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(email);
        return Task.FromResult(SpamAnalyzer.Rows(email.Headers));
    }
}
