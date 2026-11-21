// Mirrors: Bulwark components/trusted-senders-modal.tsx (the list and its add row in the app's dialog card); M2 plan section 4.2 (a
// content record per dialog lives with its owner). Owner reader, I-209.

using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Settings;

/// <summary>The Trusted senders dialog: its list model is <paramref name="ViewModel"/>; the answer is true once the user has seen the list.</summary>
/// <param name="ViewModel">The list, add row and removal of <c>trusted-senders.json</c>.</param>
public sealed record TrustedSendersDialogRequest(TrustedSendersViewModel ViewModel) : DialogRequest<bool>("Trusted senders");
