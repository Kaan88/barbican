// Mirrors: Bulwark components/email/view-source-dialog.tsx (the raw message in a monospace block with Copy and Download); Rust reader.rs SOURCE_DISPLAY_LIMIT
// :19 and source_shown :252 (more than 2 MiB is cut in the view; the whole message is one Save away). Owner reader. I-183.

using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Reader;

/// <summary>The View source dialog: the (possibly cut) source text and what the buttons do with it.</summary>
/// <param name="Title">The heading.</param>
/// <param name="Text">The source as shown, at most <c>SourceCut.DisplayLimit</c> bytes.</param>
/// <param name="Cut">The text was cut for display.</param>
/// <param name="Copy">Copies the shown text.</param>
/// <param name="SaveEml">Saves the whole message (not the cut text) as .eml.</param>
public sealed record SourceDialogRequest(string Title, string Text, bool Cut, Action<string> Copy, Func<CancellationToken, Task> SaveEml) : DialogRequest<bool>(Title);
