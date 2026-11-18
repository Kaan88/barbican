// Mirrors: Bulwark components/files/upload-conflict-dialog.tsx; G-016 (policy in files-data), D-M5-14. Drawn by the App FileConflictDialogContent. Owner files-ui.

using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Files;

/// <summary>Asks what to do with an upload whose name is taken.</summary>
public sealed record FileConflictDialogRequest(string Name) : DialogRequest<FileConflictAnswer>("File already exists");
