// Mirrors: Bulwark components/files/move-dialog.tsx; spec/menus.md "Move to" (valid targets only: My files and folders not inside the item). Drawn by the App FileMoveDialogContent. Owner files-ui.

using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Files;

/// <summary>Asks for a destination folder; <see cref="Folders"/> are the valid targets with their depth.</summary>
public sealed record FileMoveDialogRequest(IReadOnlyList<FileMoveTarget> Folders) : DialogRequest<FileMoveAnswer>("Move to");
