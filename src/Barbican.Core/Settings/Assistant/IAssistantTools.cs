// Mirrors: Rust agent.rs tool set (B-AI-008: search and read mail and threads, list folders, move, archive, delete (to Trash), mark, tag, create folder, draft and send, get and set vacation, read attachment, list, add and remove filter rules (B-SIEVE-027), change a setting, remember). The tools call IVacationService and IFilterService (srv-data) and the mail services, never raw JMAP.
// Contract from the M6 plan section 4.4; owner set-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Settings.Assistant;

/// <summary>The tools of the agent.</summary>
public interface IAssistantTools
{
    /// <summary>The specs sent to the model; with <see cref="AssistantActionMode.Off"/> only the read-only ones.</summary>
    IReadOnlyList<AssistantToolSpec> Specs(AssistantActionMode mode);

    /// <summary>Runs one tool for the active account and returns its text result (clipped to 40 000 characters, B-AI-007); a failure is a text the model can read, not an exception.</summary>
    Task<string> RunAsync(string accountKey, string name, JsonObject args, CancellationToken ct);
}
