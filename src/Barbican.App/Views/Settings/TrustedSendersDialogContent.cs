// Mirrors: Bulwark components/trusted-senders-modal.tsx (a list of senders removable one by one, the add row and its errors, the empty
// state); the dialog card, scrim and focus handling are the shared host's (PORT-PLAN section 3.7). Owner reader, I-209.

using Barbican.App.Views.Dialogs;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Settings;

namespace Barbican.App.Views.Settings;

/// <summary>Draws <see cref="TrustedSendersDialogRequest"/>: the trusted senders, removable, and the add row.</summary>
public sealed class TrustedSendersDialogContent : IDialogContent
{
    /// <inheritdoc />
    public bool OwnButtons => true;

    /// <inheritdoc />
    public double MaxWidth => 520;

    /// <inheritdoc />
    public bool CanDraw(object request) => request is TrustedSendersDialogRequest;

    /// <inheritdoc />
    public Control? InitialFocus(UIElement content) => (content as TrustedSendersDialogView)?.FocusTarget;

    /// <inheritdoc />
    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return new TrustedSendersDialogView(dialog, ((TrustedSendersDialogRequest)dialog.Request).ViewModel);
    }
}
