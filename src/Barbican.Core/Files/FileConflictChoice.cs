// Mirrors: Bulwark components/files/upload-conflict-dialog.tsx, lib/files-upload-conflicts.ts. G-016. Contract from the M5 plan section 4.9; owner files-data (files-ui draws the dialog).

namespace Barbican.Core.Files;

/// <summary>What the user chose for an upload whose name is taken (Apply to all is the caller's).</summary>
public enum FileConflictChoice
{
    KeepBoth,
    Replace,
    Skip,
}
