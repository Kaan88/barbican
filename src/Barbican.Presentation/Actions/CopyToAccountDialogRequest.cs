// Mirrors: Bulwark components/email/email-context-menu.tsx:371-379 and :410-415 (the "Copy to account" list: a heading per other account above its
// folders), hooks/use-message-file-actions.ts:181-195; PORT-PLAN section 3.7 (an owner's dialog through the one in-window host). G-048. Owner actions.

using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Actions;

/// <summary>The copy picker: the other signed-in accounts with their folders; picking one copies the messages there (null cancels).</summary>
/// <param name="Accounts">The accounts the picker offers.</param>
public sealed record CopyToAccountDialogRequest(IReadOnlyList<CopyTargetAccount> Accounts) : DialogRequest<CopyToAccountAnswer>("Copy to account");
