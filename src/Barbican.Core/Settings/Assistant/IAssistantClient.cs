// Mirrors: Rust ai.rs complete (OpenAI-compatible and Anthropic Messages, 90 s timeout, B-AI-001..003); Microsoft.Extensions.AI IChatClient may sit behind it. THREADING: completes off the UI thread. Contract from the M6 plan section 4.4; owner set-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Assistant;

/// <summary>One model call.</summary>
public interface IAssistantClient
{
    /// <summary>Sends the conversation and the tools; the answer is the assistant turn (text and/or tool calls). Errors are <see cref="AssistantErrors"/> messages.</summary>
    Task<Result<AssistantMessage>> CompleteAsync(AssistantConfig config, IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantToolSpec> tools, CancellationToken ct);
}
