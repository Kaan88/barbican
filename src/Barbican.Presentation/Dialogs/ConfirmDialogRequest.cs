// Mirrors: Rust ctxmenu.rs confirm :697 ("Are you sure?", the danger label, Cancel); PORT-PLAN section 3.7. Owner menus.

namespace Barbican.Presentation.Dialogs;

/// <summary>A yes-or-no dialog; the answer is true to go ahead, null when cancelled.</summary>
/// <param name="Title">The heading.</param>
/// <param name="Message">What will happen.</param>
/// <param name="ConfirmLabel">The button that goes ahead.</param>
/// <param name="Danger">The button is drawn as a danger button.</param>
public sealed record ConfirmDialogRequest(string Title, string Message, string ConfirmLabel = "OK", bool Danger = false) : DialogRequest<bool>(Title);
