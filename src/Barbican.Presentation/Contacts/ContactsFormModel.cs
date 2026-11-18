// Mirrors: Rust contacts_x.rs:121 fields_of, :190 build_card (the form rows and the card built from them, unknown properties kept), :565 shrink_photo; Bulwark components/contacts/contact-form.tsx (structured entries, categories, address book, photo);
// B-CON-002, B-CON-009..012, B-CON-015; I-363..I-367. The entries are con-data's ContactField rows (sections email, phone, address, url, im, date, related; Kind and Kind2 index ContactOptions); a save sends only the properties that changed (JSON-pointer patch, keywords whole). Owner con-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Contacts;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Presentation.Contacts;

/// <summary>The contact form: a card (or a new one) as a name block, typed entries, categories, a book and a photo.</summary>
public sealed class ContactsFormModel : ContactsDialogModel
{
    /// <summary>The entry sections the form offers, in the order of the form.</summary>
    public static IReadOnlyList<(string Section, string Title, string Add)> Sections { get; } =
    [
        ("email", "Email", "Add email"),
        ("phone", "Phone", "Add phone"),
        ("address", "Address", "Add address"),
        ("url", "Website", "Add website"),
        ("im", "Messaging", "Add messaging"),
        ("date", "Dates", "Add date"),
        ("related", "Related people", "Add related person"),
    ];

    private readonly IContactsViewModel _contacts;
    private readonly IContactWriter _writer;
    private readonly IContactsFilePicker _picker;
    private readonly JsonNode? _original;
    private readonly string? _cardId;
    private ContactForm _form = new();
    private readonly List<ContactField> _fields = [];
    private string _categories = string.Empty;
    private string? _bookId;
    private string? _photo;
    private bool _photoChanged;

    public ContactsFormModel(IContactsViewModel contacts, IContactWriter writer, IContactsFilePicker picker, string? cardId, string? bookId, ContactForm? seed = null, string? email = null)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(picker);
        _contacts = contacts;
        _writer = writer;
        _picker = picker;
        _cardId = cardId;
        _original = cardId is null ? null : contacts.CardOf(cardId);
        _bookId = bookId ?? contacts.Books.FirstOrDefault(b => b.IsDefault)?.Id ?? (contacts.Books.Count > 0 ? contacts.Books[0].Id : null);
        if (_original is { } card)
        {
            _form = FormOf(card);
            _categories = string.Join(", ", ContactsJson.Keys(card, "keywords"));
            _photo = ContactsJson.PhotoUri(card);
            _bookId = ContactsJson.Keys(card, "addressBookIds") is [var first, ..] ? first : _bookId;
            try
            {
                _fields.AddRange(ContactFields.Of(card));
            }
            catch (PortPendingException e)
            {
                Error = "The entries cannot be shown yet (" + e.Message + ")";
            }
        }
        else
        {
            _form = seed ?? new ContactForm();
            if (!string.IsNullOrWhiteSpace(email))
            {
                _fields.Add(new ContactField { Section = "email", A = email.Trim() });
            }
        }
    }

    public bool IsNew => _original is null;

    public string Heading => IsNew ? "New contact" : "Edit contact";

    public ContactForm Form
    {
        get => _form;
        set => SetProperty(ref _form, value);
    }

    public IReadOnlyList<ContactField> Fields => _fields;

    public string Categories
    {
        get => _categories;
        set => SetProperty(ref _categories, value ?? string.Empty);
    }

    public string? BookId
    {
        get => _bookId;
        set => SetProperty(ref _bookId, value);
    }

    public IReadOnlyList<ContactsSidebarEntry> BookChoices => _contacts.Books;

    /// <summary>The photo as a data: URI, or null.</summary>
    public string? PhotoUri => _photo;

    /// <summary>The initials the avatar of the form shows without a photo.</summary>
    public string Initials => ContactsListRules.Initials((_form.Given + " " + _form.Surname).Trim(), _fields.FirstOrDefault(f => string.Equals(f.Section, "email", StringComparison.Ordinal))?.A ?? string.Empty);

    /// <summary>The kind choices of a section (the labels of the chips).</summary>
    public static IReadOnlyList<string> KindsOf(string section) => section switch
    {
        "phone" => [.. ContactOptions.PhoneFeatures.Select(f => f.Label)],
        "date" => [.. ContactOptions.DateKinds.Select(d => d.Label)],
        "related" => [.. ContactOptions.Relations.Select(r => r.Label)],
        _ => ContactOptions.Contexts,
    };

    public void AddField(string section)
    {
        _fields.Add(new ContactField { Section = section });
        OnPropertyChanged(nameof(Fields));
    }

    public void RemoveField(int index)
    {
        if (index >= 0 && index < _fields.Count)
        {
            _fields.RemoveAt(index);
            OnPropertyChanged(nameof(Fields));
        }
    }

    public void UpdateField(int index, Func<ContactField, ContactField> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (index >= 0 && index < _fields.Count)
        {
            _fields[index] = change(_fields[index]);
            OnPropertyChanged(nameof(Initials));
        }
    }

    public async Task PickPhotoAsync(CancellationToken ct)
    {
        var picked = await _picker.OpenImageAsync(ct).ConfigureAwait(true);
        if (picked is null)
        {
            return;
        }

        try
        {
            var shrunk = ContactPhoto.Shrink(picked.Bytes);
            if (!shrunk.IsOk)
            {
                Error = Words(shrunk.Error!);
                return;
            }

            _photo = "data:" + shrunk.Value!.MediaType + ";base64," + Convert.ToBase64String(shrunk.Value.Bytes);
            _photoChanged = true;
            Error = string.Empty;
            OnPropertyChanged(nameof(PhotoUri));
        }
        catch (PortPendingException e)
        {
            Error = "Photos cannot be added yet (" + e.Message + ")";
        }
    }

    public void RemovePhoto()
    {
        _photo = null;
        _photoChanged = true;
        OnPropertyChanged(nameof(PhotoUri));
    }

    /// <summary>The card the form describes (without saving), or the validation error.</summary>
    public Result<JsonObject> BuildCard()
    {
        Result<JsonObject> built;
        try
        {
            built = ContactFields.Build(_original, _form, _fields);
        }
        catch (PortPendingException e)
        {
            return Result<JsonObject>.Fail(new ValidationError("Saving is not available yet (" + e.Message + ")"));
        }

        if (!built.IsOk)
        {
            return built;
        }

        var card = built.Value!;
        var words = _categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length > 0)
        {
            var map = new JsonObject();
            foreach (var w in words)
            {
                map[w] = true;
            }

            card["keywords"] = map;
        }
        else
        {
            _ = card.Remove("keywords");
        }

        if (_photoChanged)
        {
            if (_photo is null)
            {
                _ = card.Remove("media");
            }
            else
            {
                var media = card["media"] as JsonObject ?? [];
                foreach (var key in media.Where(p => string.Equals(ContactsJson.Str(p.Value, "kind"), "photo", StringComparison.Ordinal)).Select(p => p.Key).ToList())
                {
                    _ = media.Remove(key);
                }

                media["photo"] = new JsonObject { ["@type"] = "Media", ["kind"] = "photo", ["uri"] = _photo };
                card["media"] = media;
            }
        }

        return Result<JsonObject>.Ok(card);
    }

    public async Task<Result<Unit>> SaveAsync(CancellationToken ct)
    {
        Error = string.Empty;
        var built = BuildCard();
        if (!built.IsOk)
        {
            Error = Words(built.Error!);
            return Result<Unit>.Fail(built.Error!);
        }

        if (_contacts.AccountKey is not { } key)
        {
            return Result<Unit>.Ok(default);
        }

        Busy = true;
        try
        {
            Result<Unit> result;
            if (_cardId is null)
            {
                var created = await _writer.CreateAsync(key, _bookId, built.Value!, ct).ConfigureAwait(true);
                result = created.Map(_ => default(Unit));
            }
            else
            {
                result = await _writer.UpdateAsync(key, _cardId, Patch(_original, built.Value!), ct).ConfigureAwait(true);
            }

            if (result.IsOk)
            {
                await _contacts.RefreshAsync(ct).ConfigureAwait(true);
                Close(true);
            }
            else
            {
                Error = Words(result.Error!);
            }

            return result;
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>The properties of <paramref name="next"/> that differ from the card on the server, as a patch (a removed property is null); server-owned ones are never sent.</summary>
    public static JsonObject Patch(JsonNode? original, JsonObject next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var patch = new JsonObject();
        var old = original as JsonObject ?? [];
        foreach (var (name, value) in next)
        {
            if (IsServerOwned(name))
            {
                continue;
            }

            if (!old.TryGetPropertyValue(name, out var before) || !JsonNode.DeepEquals(before, value))
            {
                patch[name] = value?.DeepClone();
            }
        }

        foreach (var (name, _) in old)
        {
            if (!IsServerOwned(name) && !next.ContainsKey(name))
            {
                patch[name] = null;
            }
        }

        return patch;
    }

    private static bool IsServerOwned(string name) => name is "id" or "uid" or "created" or "updated" or "addressBookIds" or "@type" or "version" or "kind" or "members";

    /// <summary>The name and single fields of a card as the form shows them.</summary>
    public static ContactForm FormOf(JsonNode card)
    {
        ArgumentNullException.ThrowIfNull(card);
        string Part(params string[] kinds) => card["name"]?["components"] is JsonArray comps
            ? string.Join(' ', kinds.SelectMany(k => comps.Where(c => string.Equals(ContactsJson.Str(c, "kind"), k, StringComparison.Ordinal)).Select(c => ContactsJson.Str(c, "value"))).Where(v => v.Length > 0))
            : string.Empty;
        var given = Part("given");
        var surname = Part("surname");
        if (given.Length == 0 && surname.Length == 0 && ContactsJson.Str(card["name"], "full") is { Length: > 0 } full)
        {
            var words = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            given = words.Length > 1 ? string.Join(' ', words[..^1]) : full;
            surname = words.Length > 1 ? words[^1] : string.Empty;
        }

        return new ContactForm
        {
            Prefix = Part("title", "prefix"),
            Given = given,
            Middle = Part("given2", "middle"),
            Surname = surname,
            Suffix = Part("generation", "suffix"),
            Nickname = ContactsJson.FirstOf(card["nicknames"], "name"),
            Org = ContactsJson.FirstOf(card["organizations"], "name"),
            Dept = card["organizations"] is JsonObject orgs && orgs.FirstOrDefault().Value?["units"] is JsonArray units && units.Count > 0 ? ContactsJson.Str(units[0], "name") : string.Empty,
            JobTitle = ContactsJson.FirstOf(card["titles"], "name"),
            Role = ContactsJson.FirstOf(card["roles"], "name"),
            Notes = ContactsJson.FirstOf(card["notes"], "note"),
        };
    }
}
