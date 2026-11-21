// Mirrors: Rust agent.rs draft_email (opens the composer for review, B-AI-008). The host tool `draft_email` goes through this seam; the app registers the bridge over the real composer.

using Barbican.Core.Common;
using Barbican.Core.Compose;

namespace Barbican.Core.Plugins.Native.Host;

/// <summary>Opens a prefilled composer for review without sending mail.</summary>
public interface IPluginComposer
{
    /// <summary>Opens the composer with the request.</summary>
    Task<Result<Unit>> OpenAsync(ComposeRequest request, CancellationToken ct);
}
