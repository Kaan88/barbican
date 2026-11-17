// Mirrors: M2 plan 4.2 (dialogs through IDialogService, a custom in-window host); nothing destructive runs when nobody can be asked. Owner actions.

using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Actions;

/// <summary>Asks through the dialog host; with no host (or a dialog already open) the answer is no, so a delete never goes ahead unasked.</summary>
public sealed class DialogActionConfirmer(Func<IDialogService?> dialogs) : IActionConfirmer
{
    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(ConfirmRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var host = dialogs();
        return host is not null && await host.ShowAsync(request, ct).ConfigureAwait(false);
    }
}
