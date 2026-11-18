// Mirrors: Bulwark components/contacts/contact-form.tsx (opened from the Contacts app and from the reader's "Add to contacts"); M5 plan section 6.2 step 3. The default IContactsEditors: loads the login's contacts when they are not there yet (the form needs the books and the card), then shows the form dialog.

using Barbican.Core.Contacts;
using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Contacts;

/// <summary>Opens the contact form through the dialog service.</summary>
public sealed class ContactsEditors(IContactsViewModel vm, Barbican.Core.Contacts.IContactWriter writer, IContactsFilePicker picker, IDialogService dialogs) : IContactsEditors
{
    /// <summary>The form that was opened last (a hidden run fills it in and saves it).</summary>
    public ContactsFormModel? LastForm { get; private set; }

    public async Task OpenNewAsync(string? bookId, ContactForm? seed, string? email, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(true);
        LastForm = new ContactsFormModel(vm, writer, picker, null, bookId, seed, email);
        _ = await dialogs.ShowAsync(new ContactsFormRequest(LastForm), ct).ConfigureAwait(true);
    }

    public async Task<bool> OpenEditAsync(string cardId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cardId);
        await EnsureLoadedAsync(ct).ConfigureAwait(true);
        if (vm.CardOf(cardId) is null)
        {
            return false;
        }

        LastForm = new ContactsFormModel(vm, writer, picker, cardId, null);
        _ = await dialogs.ShowAsync(new ContactsFormRequest(LastForm), ct).ConfigureAwait(true);
        return true;
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (vm.AccountKey is { } key && !vm.IsLoadedFor(key))
        {
            await vm.RefreshAsync(ct).ConfigureAwait(true);
        }
    }
}
