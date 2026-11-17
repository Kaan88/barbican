// Mirrors: Rust ctxmenu.rs confirm :697 (title, text, the verb on the button), Bulwark components/ui/confirm-dialog.tsx. The dialog host (stream menus) renders it;
// the content record lives with its owner (M2 plan 4.2). Owner actions.

using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Actions;

/// <summary>A yes-or-no question about a destructive action; the dialog answers true to go ahead.</summary>
/// <param name="Title">The dialog title ("Delete 3 messages?").</param>
/// <param name="Message">One sentence of what happens.</param>
/// <param name="AcceptLabel">The verb on the confirming button ("Delete").</param>
/// <param name="Danger">The button is the red one.</param>
public sealed record ConfirmRequest(string Title, string Message, string AcceptLabel, bool Danger = true) : DialogRequest<bool>(Title);
