// Mirrors: Rust agent.rs (per-account history in the profile, "memory" notes, B-AI-006, B-AI-011), ui assistant column (motion.md: fade). What the assistant column and the settings page talk to. THREADING: AskAsync runs off the UI thread; Changed (history grew, a tool ran, progress text) is raised on a pool thread.
// Contract from the M6 plan section 4.4; owner set-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Assistant;

/// <summary>One account's assistant conversation.</summary>
public interface IAssistantSession
{
    /// <summary>The conversation so far (a snapshot).</summary>
    IReadOnlyList<AssistantMessage> History(string accountKey);

    /// <summary>Asks a question; <paramref name="openMessageId"/> is "this email" (id <c>current</c>, B-AI-010). Returns the answer text; the history is saved.</summary>
    Task<Result<string>> AskAsync(string accountKey, string question, string? openMessageId, CancellationToken ct);

    /// <summary>Forgets the conversation of the account.</summary>
    Task<Result<Unit>> ClearAsync(string accountKey, CancellationToken ct);

    /// <summary>Raised when the history changed or a progress line arrived, on a pool thread.</summary>
    event EventHandler? Changed;
}
