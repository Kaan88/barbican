// Mirrors: Rust ai.rs:189-191 (the 30 000 character clip), :197-222 (the system and user prompts, the <email> and <draft> tags), agent.rs system prompt (B-AI-004, B-AI-010). Contract from the M6 plan section 4.4; owner set-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Assistant;

/// <summary>Prompt building (the injection guard).</summary>
public static class AssistantPrompt
{
    /// <summary>The longest mail or draft text sent to a model.</summary>
    public const int ClipLength = 30_000;

    /// <summary>The text clipped to <see cref="ClipLength"/> characters (never inside a surrogate pair), with " [truncated]" appended when it was cut.</summary>
    public static string Clip(string text) => throw new PortPendingException("AssistantPrompt.Clip");

    /// <summary>Mail text inside <c>&lt;email&gt;</c> tags, clipped; a closing tag inside the text is defanged so the data cannot end its own block.</summary>
    public static string WrapEmail(string text) => throw new PortPendingException("AssistantPrompt.WrapEmail");

    /// <summary>The same for a draft, in <c>&lt;draft&gt;</c> tags.</summary>
    public static string WrapDraft(string text) => throw new PortPendingException("AssistantPrompt.WrapDraft");
}
