// Mirrors: Rust mail.rs confirm_action :940 (asks before deleting several messages, with confirmDelete / confirmArchive), ctxmenu.rs confirm :697 (empty folder,
// delete folder); Bulwark lib/mail-removal-confirm.ts. Owner actions.

namespace Barbican.Presentation.Actions;

/// <summary>Asks the user before something destructive; false when they decline or no one can be asked.</summary>
public interface IActionConfirmer
{
    /// <summary>Shows <paramref name="request"/> and returns whether the user went ahead.</summary>
    Task<bool> ConfirmAsync(ConfirmRequest request, CancellationToken ct);
}
