// Mirrors: Rust ctxmenu.rs PROMPT :32 (new folder, rename, new tag: one text field, OK and Cancel); PORT-PLAN section 3.7 (a custom dialog host, never ContentDialog). Owner menus.

namespace Barbican.Presentation.Dialogs;

/// <summary>A dialog with one text field; the answer is the trimmed text, or null when cancelled.</summary>
/// <param name="Title">The heading.</param>
/// <param name="Label">What the field is for.</param>
/// <param name="Initial">The text it starts with.</param>
/// <param name="ConfirmLabel">The OK button.</param>
public sealed record PromptDialogRequest(string Title, string Label, string Initial = "", string ConfirmLabel = "OK") : DialogRequest<string>(Title);
