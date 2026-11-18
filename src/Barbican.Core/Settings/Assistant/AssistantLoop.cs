// Mirrors: Rust agent.rs:493-531 (at most 16 model turns, "Stopped after too many steps."; cancel checked each turn, "Stopped."; intermediate text reported as progress; B-AI-007), :confirmation rules (B-AI-009: Ask mode asks before a write tool, send_email always asks, a declined action returns "The user declined this action. Do not retry it unless they ask.").
// A static method over its collaborators so the loop is testable with a scripted client. Contract from the M6 plan section 4.4; owner set-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Assistant;

/// <summary>The agent loop.</summary>
public static class AssistantLoop
{
    /// <summary>The most model turns one question may take.</summary>
    public const int MaxTurns = 16;

    /// <summary>Runs the conversation to an answer: calls the model, runs the tools it asks for (asking <paramref name="confirm"/> first when the mode says so), appends everything to <paramref name="history"/>, and returns the final text. <paramref name="progress"/> gets the text of turns that also ask for tools.</summary>
    public static Task<Result<string>> RunAsync(
        IAssistantClient client,
        IAssistantTools tools,
        AssistantConfig config,
        string accountKey,
        IList<AssistantMessage> history,
        Func<string, string, Task<bool>> confirm,
        Action<string> progress,
        CancellationToken ct) => throw new PortPendingException("AssistantLoop.RunAsync");
}
