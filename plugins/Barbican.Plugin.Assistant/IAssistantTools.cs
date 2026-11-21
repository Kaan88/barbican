// Mirrors: Rust agent.rs tool set (B-AI-008). The tools come from the host (IPluginHostTools); this is the add-on's view of them plus its own `remember` tool.

using System.Text.Json.Nodes;

namespace Barbican.Plugin.Assistant;

/// <summary>The tools of the agent.</summary>
public interface IAssistantTools
{
    /// <summary>The specs sent to the model for this account; with <see cref="AssistantActionMode.Off"/> only the read-only ones.</summary>
    IReadOnlyList<AssistantToolSpec> Specs(string accountKey, AssistantActionMode mode);

    /// <summary>Runs one tool for the account and returns its text result; a failure is a text the model can read, not an exception.</summary>
    Task<string> RunAsync(string accountKey, string name, JsonObject args, CancellationToken ct);
}
