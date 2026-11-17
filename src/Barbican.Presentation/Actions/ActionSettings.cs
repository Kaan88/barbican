// Mirrors: Bulwark settings-store keys archiveMode, confirmBulkDelete, deleteAction, permanentlyDeleteJunk, returnToListAfterAction, confirmDelete and confirmArchive
// (Settings > Accessibility); Rust mail.rs confirm_action :940, apply_confirmed :978 (archiveMode :990, deleteAction :1029). Owner actions.

using Barbican.Core.Settings.Accessibility;
using Barbican.Core.Storage;

namespace Barbican.Presentation.Actions;

/// <summary>The settings that decide how an action runs.</summary>
public sealed record ActionSettings
{
    /// <summary>single, year or month.</summary>
    public string ArchiveMode { get; init; } = "single";

    /// <summary>Ask before deleting several messages.</summary>
    public bool ConfirmBulkDelete { get; init; } = true;

    /// <summary>Ask before deleting even one message (Accessibility).</summary>
    public bool ConfirmDelete { get; init; }

    /// <summary>Ask before archiving (Accessibility).</summary>
    public bool ConfirmArchive { get; init; }

    /// <summary>trash, trash-and-read or permanent.</summary>
    public string DeleteAction { get; init; } = "trash";

    /// <summary>Delete in Junk is permanent.</summary>
    public bool PermanentlyDeleteJunk { get; init; }

    /// <summary>Reads the settings (the defaults for anything not stored).</summary>
    public static ActionSettings Read(PrefsSnapshot prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        var a11y = A11y.From(prefs, new NoSystemAccessibility().Current);
        return new ActionSettings
        {
            ArchiveMode = prefs.Str("archiveMode", "single"),
            ConfirmBulkDelete = prefs.Bool("confirmBulkDelete", true),
            ConfirmDelete = a11y.ConfirmDelete,
            ConfirmArchive = a11y.ConfirmArchive,
            DeleteAction = prefs.Str("deleteAction", "trash"),
            PermanentlyDeleteJunk = prefs.Bool("permanentlyDeleteJunk", false),
        };
    }
}
