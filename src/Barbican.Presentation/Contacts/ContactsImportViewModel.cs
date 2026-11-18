// Mirrors: Rust contacts_x.rs:752 import_batch (duplicates by email, skip, one book), Bulwark components/contacts/contact-import-dialog.tsx (the preview, the duplicates list, the book picker, progress), lib/vcard.ts; B-CON-007, B-CON-008, B-CON-016; I-361; L-031.
// The file is read through ParseChecked (G-012: size, card and photo limits); nothing is written before the user presses Import. Owner con-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Contacts;

namespace Barbican.Presentation.Contacts;

/// <summary>The import dialog: pick a .vcf, see what it holds, choose the address book and whether duplicates are skipped, import in chunks.</summary>
public sealed class ContactsImportViewModel : ContactsDialogModel
{
    private readonly IContactsViewModel _contacts;
    private readonly IContactWriter _writer;
    private readonly IAddressBooks _books;
    private readonly IContactsFilePicker _picker;
    private IReadOnlyList<JsonNode> _cards = [];
    private IReadOnlyList<int> _duplicates = [];
    private string _fileName = string.Empty;
    private bool _skip = true;
    private string? _bookId;

    public ContactsImportViewModel(IContactsViewModel contacts, IContactWriter writer, IAddressBooks books, IContactsFilePicker picker)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(books);
        ArgumentNullException.ThrowIfNull(picker);
        _contacts = contacts;
        _writer = writer;
        _books = books;
        _picker = picker;
        _bookId = contacts.Books.FirstOrDefault(b => b.IsDefault)?.Id ?? (contacts.Books.Count > 0 ? contacts.Books[0].Id : null);
    }

    public string FileName => _fileName;

    /// <summary>The cards the file holds (after the limits).</summary>
    public IReadOnlyList<JsonNode> Cards => _cards;

    /// <summary>Indexes of the cards whose email is already known.</summary>
    public IReadOnlyList<int> Duplicates => _duplicates;

    public bool SkipDuplicates
    {
        get => _skip;
        set => SetProperty(ref _skip, value);
    }

    /// <summary>The book the cards go to; null until one exists (a fresh login: the default book is created on import).</summary>
    public string? BookId
    {
        get => _bookId;
        set => SetProperty(ref _bookId, value);
    }

    public IReadOnlyList<ContactsSidebarEntry> BookChoices => _contacts.Books;

    /// <summary>How many cards an import would create now.</summary>
    public int ImportCount => _skip ? _cards.Count - _duplicates.Count : _cards.Count;

    public async Task PickAsync(CancellationToken ct)
    {
        Error = string.Empty;
        var file = await _picker.OpenVcardAsync(ct).ConfigureAwait(true);
        if (file is not null)
        {
            Load(file.Name, file.Text);
        }
    }

    /// <summary>Reads the text of a file (also what a drop or a hidden run feeds in).</summary>
    public void Load(string name, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _fileName = name;
        Result<VCardParseResult> parsed;
        try
        {
            parsed = VCardParser.ParseChecked(text, new VCardLimits());
        }
        catch (PortPendingException e)
        {
            Error = "Importing is not available yet (" + e.Message + ")";
            return;
        }

        if (!parsed.IsOk)
        {
            _cards = [];
            _duplicates = [];
            Error = Words(parsed.Error!);
            OnPropertyChanged(string.Empty);
            return;
        }

        var result = parsed.Value!;
        _cards = result.Cards;
        _duplicates = ContactImport.DetectDuplicates(_contacts.Contacts, _cards);
        var notes = new List<string> { _cards.Count + " contacts in " + name };
        if (_duplicates.Count > 0)
        {
            notes.Add(_duplicates.Count + " already in your contacts");
        }

        if (result.SkippedCards > 0)
        {
            notes.Add(result.SkippedCards + " skipped (over the limit)");
        }

        if (result.PhotosDropped > 0)
        {
            notes.Add(result.PhotosDropped + " photos too large, left out");
        }

        Status = string.Join(". ", notes) + ".";
        Error = _cards.Count == 0 ? "No contacts found in that file." : string.Empty;
        OnPropertyChanged(string.Empty);
    }

    public async Task<Result<int>> ImportAsync(CancellationToken ct)
    {
        if (_contacts.AccountKey is not { } key || _cards.Count == 0)
        {
            return Result<int>.Ok(0);
        }

        Busy = true;
        Error = string.Empty;
        try
        {
            var book = _bookId;
            if (book is null)
            {
                var ensured = await _books.EnsureDefaultAsync(key, ct).ConfigureAwait(true);
                if (!ensured.IsOk)
                {
                    Error = Words(ensured.Error!);
                    return Result<int>.Fail(ensured.Error!);
                }

                book = ensured.Value!;
            }

            var batch = ContactImport.Batch(_cards, _duplicates, _skip, book);
            var result = await _writer.ImportAsync(key, book, batch, ct).ConfigureAwait(true);
            if (result.IsOk)
            {
                Status = "Imported " + result.Value + " contacts.";
                await _contacts.RefreshAsync(ct).ConfigureAwait(true);
                Close(true);
            }
            else
            {
                Error = "Imported " + result.Value + " of " + batch.Count + " contacts, then: " + Words(result.Error!);
            }

            return result;
        }
        finally
        {
            Busy = false;
        }
    }
}
