// Mirrors: Bulwark components/contacts/contact-form.tsx (open for a new or an existing contact); Rust contacts.rs editor. M5 plan section 6.2 step 2. Owner con-ui: the seam between the commands (Presentation) and the form dialog (App).

using Barbican.Core.Contacts;

namespace Barbican.Presentation.Contacts;

/// <summary>Opens the contact form.</summary>
public interface IContactsEditors
{
    /// <summary>A new contact in <paramref name="bookId"/> (null: the default book), optionally pre-filled (a name and an email from the reader).</summary>
    Task OpenNewAsync(string? bookId, ContactForm? seed, string? email, CancellationToken ct);

    /// <summary>The form for an existing contact; false when the card is not there.</summary>
    Task<bool> OpenEditAsync(string cardId, CancellationToken ct);
}
