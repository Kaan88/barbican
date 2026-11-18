// Mirrors: Rust agent.rs draft_email (opens the composer for review, B-AI-008).
using Barbican.Core.Common;
using Barbican.Core.Compose;

namespace Barbican.Core.Settings.Assistant;

/// <summary>Opens a prefilled composer for review without sending mail.</summary>
public interface IAssistantComposer
{
    Task<Result<Unit>> OpenAsync(ComposeRequest request, CancellationToken ct);
}
