// Mirrors: Rust ai.rs:25-65 (Config load and save: provider, base, model, language, actions, memory, instructions in the desktop settings.json; the key in the credential store), memory.txt (the notes); B-AI-002, B-AI-006, B-SET-039 (per account).
// The contract of the M6 plan has no way to save these (IAssistantSession only asks); this additive interface is what the assistant page of set-ui reads and writes (M6 Fable log: request). Owner set-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Assistant;

/// <summary>The assistant settings of an account (desktop settings.json, key "assistant").</summary>
public interface IAssistantSettings
{
    /// <summary>Loads profile settings off the UI thread before they are needed.</summary>
    Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>The settings of the account; defaults (OpenAI-compatible, Ask mode, memory on) when nothing is stored. A memory read.</summary>
    AssistantConfig Config(string accountKey);

    /// <summary>Stores the settings of the account (the file is written off the UI thread).</summary>
    Task<Result<Unit>> SaveAsync(string accountKey, AssistantConfig config, CancellationToken ct);

    /// <summary>Raised after a save, on a pool thread.</summary>
    event EventHandler? Changed;
}
