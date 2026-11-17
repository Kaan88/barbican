// Mirrors: see IRewriteProvider. Default registration from the M3 plan; the assistant (M6) replaces it.

namespace Barbican.Presentation.Editor;

/// <summary>The default: no assistant.</summary>
public sealed class NoRewriteProvider : IRewriteProvider
{
    public bool IsAvailable => false;

    public Task<string?> RewriteAsync(string action, string text, CancellationToken ct) => Task.FromResult<string?>(null);
}
