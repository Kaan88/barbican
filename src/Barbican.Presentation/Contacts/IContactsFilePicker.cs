// Mirrors: Rust contacts_x.rs import (rfd open .vcf), export (save), D-M5-18 (export only through the system save picker); Bulwark contact-import-dialog.tsx, contact-form.tsx (photo picker). Owner con-ui.

#pragma warning disable CA1819, MA0048 // a picked image is its bytes; the two picked-file records sit beside the picker
namespace Barbican.Presentation.Contacts;

/// <summary>A .vcf file the user picked, read as text.</summary>
public sealed record ContactsPickedFile(string Name, string Text);

/// <summary>An image the user picked for a contact photo.</summary>
public sealed record ContactsPickedImage(string Name, byte[] Bytes);

/// <summary>The system pickers of the Contacts app (a hidden run answers from queues instead).</summary>
public interface IContactsFilePicker
{
    /// <summary>The .vcf file the user chose, or null when cancelled.</summary>
    Task<ContactsPickedFile?> OpenVcardAsync(CancellationToken ct);

    /// <summary>The image the user chose for a photo, or null.</summary>
    Task<ContactsPickedImage?> OpenImageAsync(CancellationToken ct);

    /// <summary>Saves <paramref name="text"/> under a name the user chooses (suggested <paramref name="fileName"/>); the saved path, or null when cancelled.</summary>
    Task<string?> SaveTextAsync(string fileName, string text, CancellationToken ct);
}
