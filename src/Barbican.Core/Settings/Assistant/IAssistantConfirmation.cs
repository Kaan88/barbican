// Mirrors: Rust agent.rs Host::confirm (a dialog before a write tool runs in Ask mode, and before send_email always; B-AI-009). IAssistantSession.AskAsync has no way to ask, so the assistant column of set-ui registers this (M6 Fable log: request); without it every write is declined, which is the safe default. Owner set-data.

namespace Barbican.Core.Settings.Assistant;

/// <summary>Asks the user before the assistant changes something.</summary>
public interface IAssistantConfirmation
{
    /// <summary>Shows what the tool is about to do; true runs it. Called from a pool thread: the view marshals to its dispatcher.</summary>
    Task<bool> ConfirmAsync(string tool, string description, CancellationToken ct);
}
