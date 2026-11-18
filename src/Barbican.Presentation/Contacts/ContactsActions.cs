// Mirrors: Rust ctxmenu.rs:314-330, :454 (the contact, group, category and book menu actions), contacts.rs (delete, groups, categories, books), Bulwark components/contacts/contacts-app.tsx, contacts-sidebar.tsx, contact-detail.tsx, lib/contacts-actions;
// B-CON-002 (write an email), B-CON-005, B-CON-006 (categories send the whole keywords map), B-CON-016, B-CON-024; I-345..I-369; D-M5-B03 (deleting a category asks first: it edits every contact that has it). D-7 (a delete always asks). Owner con-ui.
// What each menu item, key and button does, in one place so the pointer, the keys and the automation commands take the same road. Every question goes through IDialogService (in the window, ADR 0007).

#pragma warning disable MA0048 // the dependency record sits beside the one class that takes it
using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Contacts;
using Barbican.Core.Jmap.Errors;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Common;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Contacts;

/// <summary>The dependencies of <see cref="ContactsActions"/> (optional ones are null where a surface does not exist, as in a test).</summary>
public sealed record ContactsActionDeps(
    IContactsViewModel Vm,
    IContactWriter Writer,
    IAddressBooks Books,
    IContactsFilePicker Picker,
    IContactsEditors Editors,
    IContactsChrome Chrome,
    TimeProvider Time)
{
    public IDialogService? Dialogs { get; init; }

    public IComposerService? Composer { get; init; }

    public IClipboardService? Clipboard { get; init; }

    public IToastService? Toasts { get; init; }

    public IContactDirectory? Directory { get; init; }

    public IContactLookup? Lookup { get; init; }

    public ShellViewModel? Shell { get; init; }
}

/// <summary>The contacts actions.</summary>
public sealed class ContactsActions(ContactsActionDeps d)
{
    private static readonly Result<Unit> Done = Result<Unit>.Ok(default);
    private static readonly char[] BadNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    private IContactsViewModel Vm => d.Vm;

    /// <summary>The ids a command works on: the ones in its argument, else the selection, else the open contact.</summary>
    public IReadOnlyList<string> IdsOf(string? argument)
    {
        var given = ContactsArg.Split(argument).Where(a => a.Length > 0).ToList();
        if (given.Count > 0)
        {
            return given;
        }

        if (Vm.SelectedIds.Count > 0)
        {
            return [.. Vm.SelectedIds];
        }

        return Vm.OpenId is { } open ? [open] : [];
    }

    // ---- contacts ----

    public async Task<Result<Unit>> NewContactAsync(string? bookId, CancellationToken ct)
    {
        await d.Editors.OpenNewAsync(string.IsNullOrEmpty(bookId) ? null : bookId, null, null, ct).ConfigureAwait(true);
        return Done;
    }

    public async Task<Result<Unit>> EditAsync(string? argument, CancellationToken ct)
    {
        if (IdsOf(argument) is [var id, ..])
        {
            _ = await d.Editors.OpenEditAsync(id, ct).ConfigureAwait(true);
        }

        return Done;
    }

    public Task<Result<Unit>> OpenAsync(string? argument)
    {
        if (IdsOf(argument) is [var id, ..])
        {
            Vm.Open(id);
        }

        return Task.FromResult(Done);
    }

    public async Task<Result<Unit>> DeleteAsync(string? argument, CancellationToken ct)
    {
        var ids = IdsOf(argument);
        if (ids.Count == 0)
        {
            return Done;
        }

        var name = ids.Count == 1 && Vm.CardOf(ids[0]) is { } card ? ContactsListRules.FullName(card) : null;
        var message = ids.Count == 1 ? "Delete " + name + "? This cannot be undone." : "Delete " + ids.Count.ToString(CultureInfo.InvariantCulture) + " contacts? This cannot be undone.";
        if (!await AskAsync(new ConfirmDialogRequest(ids.Count == 1 ? "Delete contact" : "Delete contacts", message, "Delete", Danger: true), ct).ConfigureAwait(true))
        {
            return Done;
        }

        var result = await Vm.DeleteAsync(ids, ct).ConfigureAwait(true);
        Report(result, ids.Count == 1 ? "Contact deleted" : ids.Count.ToString(CultureInfo.InvariantCulture) + " contacts deleted");
        return result;
    }

    public async Task<Result<Unit>> SendEmailAsync(string? argument, CancellationToken ct)
    {
        var emails = IdsOf(argument).Select(Vm.CardOf).Where(c => c is not null).Select(c => ContactsJson.Emails(c!).FirstOrDefault()).Where(e => !string.IsNullOrEmpty(e)).ToList();
        return await ComposeAsync(string.Join(", ", emails), string.Empty, string.Empty, ct).ConfigureAwait(true);
    }

    public Task<Result<Unit>> CopyEmailAsync(string? argument)
    {
        var email = IdsOf(argument).Select(Vm.CardOf).Where(c => c is not null).SelectMany(c => ContactsJson.Emails(c!).Take(1)).ToList();
        if (email.Count > 0)
        {
            d.Clipboard?.SetText(string.Join(", ", email));
        }

        return Task.FromResult(Done);
    }

    // ---- groups ----

    public async Task<Result<Unit>> ToggleGroupAsync(string? argument, CancellationToken ct)
    {
        var parts = ContactsArg.Split(argument);
        if (parts.Count < 2 || Vm.AccountKey is not { } key || Vm.CardOf(parts[0]) is not { } group)
        {
            return Done;
        }

        var cards = parts.Skip(1).Select(Vm.CardOf).Where(c => c is not null).Select(c => c!).ToList();
        var allIn = cards.Count > 0 && cards.All(c => ContactsJson.HasMember(group, c));
        var uids = cards.Select(c => ContactsJson.Str(c, "uid")).Where(u => u.Length > 0).ToList();
        var result = allIn
            ? await d.Writer.SetMembersAsync(key, parts[0], [], uids, ct).ConfigureAwait(true)
            : await d.Writer.SetMembersAsync(key, parts[0], uids, [], ct).ConfigureAwait(true);
        Report(result, null);
        return result;
    }

    public async Task<Result<Unit>> NewGroupWithAsync(string? argument, CancellationToken ct)
    {
        var ids = IdsOf(argument);
        var name = await PromptAsync(new PromptDialogRequest("New group", "Group name", string.Empty, "Create"), ct).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name) || Vm.AccountKey is not { } key)
        {
            return Done;
        }

        var uids = ids.Select(Vm.CardOf).Where(c => c is not null).Select(c => ContactsJson.Str(c!, "uid")).Where(u => u.Length > 0).ToList();
        var created = await d.Writer.CreateGroupAsync(key, null, name.Trim(), uids, ct).ConfigureAwait(true);
        var result = created.Map(_ => default(Unit));
        Report(result, "Group created");
        return result;
    }

    public async Task<Result<Unit>> NewGroupAsync(CancellationToken ct)
    {
        await ShowAsync(new ContactsGroupRequest(new ContactsGroupModel(Vm, d.Writer, null, Vm.SelectedIds)), ct).ConfigureAwait(true);
        return Done;
    }

    public async Task<Result<Unit>> EditGroupAsync(string? groupId, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(groupId))
        {
            await ShowAsync(new ContactsGroupRequest(new ContactsGroupModel(Vm, d.Writer, groupId)), ct).ConfigureAwait(true);
        }

        return Done;
    }

    public async Task<Result<Unit>> RenameGroupAsync(string? groupId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(groupId) || Vm.AccountKey is not { } key || Vm.CardOf(groupId) is not { } group)
        {
            return Done;
        }

        var name = await PromptAsync(new PromptDialogRequest("Rename group", "Group name", ContactsListRules.FullName(group), "Rename"), ct).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name))
        {
            return Done;
        }

        var result = await d.Writer.UpdateAsync(key, groupId, NamePatch(group, name.Trim()), ct).ConfigureAwait(true);
        Report(result, null);
        return result;
    }

    public async Task<Result<Unit>> DeleteGroupAsync(string? groupId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(groupId) || Vm.CardOf(groupId) is not { } group)
        {
            return Done;
        }

        if (!await AskAsync(new ConfirmDialogRequest("Delete group", "Delete the group " + ContactsListRules.FullName(group) + "? The contacts in it stay.", "Delete group", Danger: true), ct).ConfigureAwait(true))
        {
            return Done;
        }

        var result = await Vm.DeleteAsync([groupId], ct).ConfigureAwait(true);
        Report(result, "Group deleted");
        return result;
    }

    public async Task<Result<Unit>> SendGroupAsync(string? argument, CancellationToken ct)
    {
        var parts = ContactsArg.Split(argument);
        if (parts.Count < 2 || Vm.CardOf(parts[1]) is not { } group)
        {
            return Done;
        }

        var emails = string.Join(", ", Vm.Contacts.Where(c => ContactsJson.HasMember(group, c)).Select(c => ContactsJson.Emails(c).FirstOrDefault()).Where(e => !string.IsNullOrEmpty(e)));
        return parts[0] switch
        {
            "cc" => await ComposeAsync(string.Empty, emails, string.Empty, ct).ConfigureAwait(true),
            "bcc" => await ComposeAsync(string.Empty, string.Empty, emails, ct).ConfigureAwait(true),
            _ => await ComposeAsync(emails, string.Empty, string.Empty, ct).ConfigureAwait(true),
        };
    }

    // ---- categories ----

    public async Task<Result<Unit>> ToggleCategoryAsync(string? argument, CancellationToken ct)
    {
        var parts = ContactsArg.Split(argument);
        if (parts.Count < 2)
        {
            return Done;
        }

        var category = parts[0];
        var cards = parts.Skip(1).Select(Vm.CardOf).Where(c => c is not null).Select(c => c!).ToList();
        var allHave = cards.Count > 0 && cards.All(c => ContactsJson.Keys(c, "keywords").Contains(category, StringComparer.Ordinal));
        return await EditKeywordsAsync(cards, words => allHave ? words.Where(w => !string.Equals(w, category, StringComparison.Ordinal)).ToList() : [.. words.Append(category).Distinct(StringComparer.Ordinal)], ct).ConfigureAwait(true);
    }

    public async Task<Result<Unit>> NewCategoryAsync(string? argument, CancellationToken ct)
    {
        var cards = IdsOf(argument).Select(Vm.CardOf).Where(c => c is not null).Select(c => c!).ToList();
        var name = await PromptAsync(new PromptDialogRequest("New category", "Category name", string.Empty, "Add"), ct).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name))
        {
            return Done;
        }

        var category = name.Trim();
        return await EditKeywordsAsync(cards, words => [.. words.Append(category).Distinct(StringComparer.Ordinal)], ct).ConfigureAwait(true);
    }

    public async Task<Result<Unit>> RenameCategoryAsync(string? category, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(category))
        {
            return Done;
        }

        var name = await PromptAsync(new PromptDialogRequest("Rename category", "Category name", category, "Rename"), ct).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name))
        {
            return Done;
        }

        var next = name.Trim();
        var cards = Vm.Contacts.Where(c => ContactsJson.Keys(c, "keywords").Contains(category, StringComparer.Ordinal)).ToList();
        return await EditKeywordsAsync(cards, words => [.. words.Select(w => string.Equals(w, category, StringComparison.Ordinal) ? next : w).Distinct(StringComparer.Ordinal)], ct).ConfigureAwait(true);
    }

    public async Task<Result<Unit>> DeleteCategoryAsync(string? category, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(category))
        {
            return Done;
        }

        var cards = Vm.Contacts.Where(c => ContactsJson.Keys(c, "keywords").Contains(category, StringComparer.Ordinal)).ToList();
        if (!await AskAsync(new ConfirmDialogRequest("Delete category", "Remove the category " + category + " from " + cards.Count.ToString(CultureInfo.InvariantCulture) + " contacts? The contacts stay.", "Delete category", Danger: true), ct).ConfigureAwait(true))
        {
            return Done;
        }

        return await EditKeywordsAsync(cards, words => [.. words.Where(w => !string.Equals(w, category, StringComparison.Ordinal))], ct).ConfigureAwait(true);
    }

    public Task<Result<Unit>> ShowPlaceAsync(ContactsPlace place, string? id)
    {
        Vm.ShowPlace(place, id);
        return Task.FromResult(Done);
    }

    // ---- address books ----

    public async Task<Result<Unit>> NewBookAsync(CancellationToken ct)
    {
        var name = await PromptAsync(new PromptDialogRequest("New address book", "Name", string.Empty, "Create"), ct).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name) || Vm.AccountKey is not { } key)
        {
            return Done;
        }

        var created = await d.Books.CreateAsync(key, name.Trim(), ct).ConfigureAwait(true);
        var result = created.Map(_ => default(Unit));
        if (result.IsOk)
        {
            await Vm.RefreshAsync(ct).ConfigureAwait(true);
        }

        Report(result, "Address book created");
        return result;
    }

    public async Task<Result<Unit>> RenameBookAsync(string? bookId, CancellationToken ct)
    {
        var book = Vm.Books.FirstOrDefault(b => string.Equals(b.Id, bookId, StringComparison.Ordinal));
        if (book is null || Vm.AccountKey is not { } key)
        {
            return Done;
        }

        var name = await PromptAsync(new PromptDialogRequest("Rename address book", "Name", book.Name, "Rename"), ct).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name))
        {
            return Done;
        }

        var result = await d.Books.RenameAsync(key, book.Id!, name.Trim(), ct).ConfigureAwait(true);
        if (result.IsOk)
        {
            await Vm.RefreshAsync(ct).ConfigureAwait(true);
        }

        Report(result, null);
        return result;
    }

    public async Task<Result<Unit>> DeleteBookAsync(string? bookId, CancellationToken ct)
    {
        var book = Vm.Books.FirstOrDefault(b => string.Equals(b.Id, bookId, StringComparison.Ordinal));
        if (book is null || book.IsDefault || Vm.AccountKey is not { } key)
        {
            return Done;
        }

        var message = book.Count == 0
            ? "Delete the address book " + book.Name + "?"
            : "Delete the address book " + book.Name + " and the " + book.Count.ToString(CultureInfo.InvariantCulture) + " contacts in it? This cannot be undone.";
        if (!await AskAsync(new ConfirmDialogRequest("Delete address book", message, "Delete address book", Danger: true), ct).ConfigureAwait(true))
        {
            return Done;
        }

        var result = await d.Books.DeleteAsync(key, book.Id!, book.Count > 0, ct).ConfigureAwait(true);
        if (result.IsOk)
        {
            Vm.ShowPlace(ContactsPlace.All, null);
            await Vm.RefreshAsync(ct).ConfigureAwait(true);
        }

        Report(result, "Address book deleted");
        return result;
    }

    // ---- import, export, merge, print ----

    public async Task<Result<Unit>> ImportAsync(CancellationToken ct)
    {
        await ShowAsync(new ContactsImportRequest(new ContactsImportViewModel(Vm, d.Writer, d.Books, d.Picker)), ct).ConfigureAwait(true);
        return Done;
    }

    /// <summary>Exports the ids (or, with none, every contact of the list shown) as one .vcf through the system save picker (D-M5-18).</summary>
    public async Task<Result<Unit>> ExportAsync(string? argument, CancellationToken ct)
    {
        var ids = ContactsArg.Split(argument).Where(a => a.Length > 0).ToList();
        var cards = (ids.Count > 0 ? ids : Vm.Rows.Select(r => r.Id).ToList()).Select(Vm.CardOf).Where(c => c is not null).Select(c => c!).ToList();
        if (cards.Count == 0)
        {
            return Done;
        }

        string text;
        try
        {
            text = VCardWriter.Generate(cards);
        }
        catch (PortPendingException e)
        {
            return Result<Unit>.Fail(new ValidationError("Exporting is not available yet (" + e.Message + ")"));
        }

        var path = await d.Picker.SaveTextAsync(ExportName(cards), text, ct).ConfigureAwait(true);
        if (path is not null)
        {
            Info(cards.Count == 1 ? "Contact exported" : cards.Count.ToString(CultureInfo.InvariantCulture) + " contacts exported");
        }

        return Done;
    }

    /// <summary>The suggested file name: the contact name for one card, else "contacts", with characters a file name cannot have replaced (the picker has the last word).</summary>
    public static string ExportName(IReadOnlyList<JsonNode> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var stem = cards.Count == 1 ? ContactsListRules.FullName(cards[0]) : "contacts";
        var clean = string.Concat(stem.Select(ch => char.IsControl(ch) || Array.IndexOf(BadNameChars, ch) >= 0 ? '_' : ch)).Trim().TrimEnd('.');
        return (clean.Length == 0 ? "contacts" : clean.Length > 80 ? clean[..80] : clean) + ".vcf";
    }

    public async Task<Result<Unit>> MergeAsync(CancellationToken ct)
    {
        if (Vm.DuplicateSetCount == 0)
        {
            Info("No duplicate contacts found");
            return Done;
        }

        await ShowAsync(new ContactsMergeRequest(new ContactsMergeViewModel(Vm, d.Writer)), ct).ConfigureAwait(true);
        return Done;
    }

    public async Task<Result<Unit>> PrintAsync(string? argument, CancellationToken ct)
    {
        var ids = IdsOf(argument);
        var cards = (ids.Count > 0 ? ids : Vm.Rows.Select(r => r.Id).ToList()).Select(Vm.CardOf).Where(c => c is not null).Select(c => c!).ToList();
        if (cards.Count == 0)
        {
            return Done;
        }

        await ShowAsync(new ContactsPrintRequest(ContactsPrintModel.Build(cards, DateOnly.FromDateTime(d.Time.GetLocalNow().DateTime))), ct).ConfigureAwait(true);
        return Done;
    }

    // ---- the directory, and the mail side ----

    /// <summary>The composer's directory button: the chosen addresses joined with commas, or null.</summary>
    public async Task<string?> PickFromDirectoryAsync(CancellationToken ct)
    {
        if (d.Directory is null || d.Dialogs is null || Vm.AccountKey is not { } key)
        {
            return null;
        }

        var model = new ContactsDirectoryModel(d.Directory, key);
        await model.LoadAsync(ct).ConfigureAwait(true);
        return await d.Dialogs.ShowAsync(new ContactsDirectoryRequest(model), ct).ConfigureAwait(true);
    }

    /// <summary>The address menu's "View contact" (I-192): opens the Contacts app on the card of that address.</summary>
    public async Task<Result<Unit>> ViewContactAsync(string? email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Done;
        }

        var match = d.Lookup is null ? null : await d.Lookup.FindByEmailAsync(email.Trim(), ct).ConfigureAwait(true);
        if (match is null)
        {
            Info("No contact has the address " + email.Trim());
            return Done;
        }

        _ = d.Shell?.TryActivate(AppId.Contacts);
        Vm.ShowPlace(ContactsPlace.All, null);
        var id = ContactsJson.Id(match.Card);
        Vm.SelectContact(id, false);
        Vm.Open(id);
        return Done;
    }

    /// <summary>The address menu's "Add to contacts" (I-193): the form with the address filled in, or the existing card when one has it.</summary>
    public async Task<Result<Unit>> AddToContactsAsync(string? email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Done;
        }

        var match = d.Lookup is null ? null : await d.Lookup.FindByEmailAsync(email.Trim(), ct).ConfigureAwait(true);
        if (match is not null)
        {
            _ = await d.Editors.OpenEditAsync(ContactsJson.Id(match.Card), ct).ConfigureAwait(true);
            return Done;
        }

        await d.Editors.OpenNewAsync(null, null, email.Trim(), ct).ConfigureAwait(true);
        return Done;
    }

    // ---- helpers ----

    private async Task<Result<Unit>> EditKeywordsAsync(IReadOnlyList<JsonNode> cards, Func<IReadOnlyList<string>, IReadOnlyList<string>> change, CancellationToken ct)
    {
        if (Vm.AccountKey is not { } key)
        {
            return Done;
        }

        var result = Done;
        foreach (var card in cards)
        {
            var words = change(ContactsJson.Keys(card, "keywords"));
            var map = new JsonObject();
            foreach (var w in words)
            {
                map[w] = true;
            }

            // keywords go whole: a pointer patch into a card without keywords is refused by the server (B-CON-006)
            result = await d.Writer.UpdateAsync(key, ContactsJson.Id(card), new JsonObject { ["keywords"] = map }, ct).ConfigureAwait(true);
            if (!result.IsOk)
            {
                break;
            }
        }

        Report(result, null);
        return result;
    }

    private static JsonObject NamePatch(JsonNode group, string name)
    {
        var next = group["name"]?.DeepClone() as JsonObject ?? [];
        next["full"] = name;
        return new JsonObject { ["name"] = next };
    }

    private async Task<Result<Unit>> ComposeAsync(string to, string cc, string bcc, CancellationToken ct)
    {
        if (d.Composer is null || Vm.AccountKey is not { } key || (to.Length + cc.Length + bcc.Length) == 0)
        {
            return Done;
        }

        await d.Composer.OpenAsync(new ComposeRequest(key, ComposeKind.New) { Prefill = new ComposePrefill { To = to, Cc = cc, Bcc = bcc } }, ct).ConfigureAwait(true);
        return Done;
    }

    private async Task<bool> AskAsync(ConfirmDialogRequest request, CancellationToken ct) => d.Dialogs is not null && await d.Dialogs.ShowAsync(request, ct).ConfigureAwait(true);

    private async Task<string?> PromptAsync(PromptDialogRequest request, CancellationToken ct) => d.Dialogs is null ? null : await d.Dialogs.ShowAsync(request, ct).ConfigureAwait(true);

    private async Task ShowAsync(DialogRequest<bool> request, CancellationToken ct)
    {
        if (d.Dialogs is not null)
        {
            _ = await d.Dialogs.ShowAsync(request, ct).ConfigureAwait(true);
        }
    }

    private void Info(string message) => _ = d.Toasts?.Show(new ToastRequest(message));

    private void Report(Result<Unit> result, string? success)
    {
        if (!result.IsOk)
        {
            _ = d.Toasts?.Show(new ToastRequest(result.Error!.Message, IsError: true));
        }
        else if (success is not null)
        {
            Info(success);
        }
    }
}
