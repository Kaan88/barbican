// Mirrors: Bulwark components/contacts/contact-form.tsx (the form is a scrollable panel, address book first, sections divided by a hairline; CT-02), Rust pim-contacts.slint editor; I-363..I-367, B-CON-009..012, B-CON-015.
// The dialog form: used when the Contacts app is not on screen (the reader's "Add to contacts") and in Bento and Nordic. It draws the same ContactsFormPanel the Contacts app shows in its pane, with its own Cancel and Save; nothing here writes: Save is the model's.

using Barbican.Presentation.Contacts;
using Barbican.Presentation.Dialogs;

namespace Barbican.App.Views.Contacts;

/// <summary>Draws <see cref="ContactsFormRequest"/>.</summary>
public sealed class ContactsFormDialogContent : Barbican.App.Views.Dialogs.IDialogContent
{
    public bool OwnButtons => true;

    public double MaxWidth => 640;

    public bool CanDraw(object request) => request is ContactsFormRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var m = ((ContactsFormRequest)dialog.Request).Model;
        m.Finished += (_, e) => dialog.Complete(e.Done);
        return new ContactsFormPanel(m, done => dialog.Complete(done), header: false) { MinWidth = 480 };
    }
}
