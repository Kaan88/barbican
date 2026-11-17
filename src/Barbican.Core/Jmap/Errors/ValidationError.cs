// Mirrors: Rust functions that return Result<_, String> for a refusal decided locally (dnd.rs:149 plan_folder_drop, reader.rs:169 one_click_post): no
// server call was made, the message is for the user. Added by stage F of M2 so the pure Core functions can use Result<T>.

namespace Barbican.Core.Jmap.Errors;

/// <summary>A refusal decided locally, before any request: the message is shown to the user.</summary>
public sealed record ValidationError(string Text) : JmapError(Text);
