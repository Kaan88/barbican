// New tests (M5 plan 6.2 acceptance): the contact actions behind the menus and keys: delete asks first (D-7), groups and categories write what the menu says (keywords whole, B-CON-006), the composer opens with the addresses, export names, the commands and menus. Owner con-ui.

#pragma warning disable MA0002, MA0006, MA0048 // ids and names are compared ordinally by nature; the scripted fakes sit beside their tests
using System.Text.Json.Nodes;
using Barbican.Core.Compose;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Contacts;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Menus;

namespace Barbican.Presentation.Tests.Contacts;

internal sealed class ScriptedDialogs : IDialogService
{
    private readonly Queue<object?> _answers = new();

    public List<object> Shown { get; } = [];

    public bool IsOpen => false;

    public void Answer(object? answer) => _answers.Enqueue(answer);

    public Task<TResult?> ShowAsync<TResult>(DialogRequest<TResult> request, CancellationToken ct)
    {
        Shown.Add(request);
        var answer = _answers.Count > 0 ? _answers.Dequeue() : null;
        return Task.FromResult(answer is TResult r ? r : default);
    }
}

internal sealed class RecordingComposer : IComposerService
{
    public List<ComposeRequest> Opened { get; } = [];

    public bool IsOpen => false;

    public event EventHandler? OpenChanged
    {
        add { }
        remove { }
    }

    public Task<ComposerViewModel?> OpenAsync(ComposeRequest request, CancellationToken ct)
    {
        Opened.Add(request);
        return Task.FromResult<ComposerViewModel?>(null);
    }

    public Task OpenSeedAsync(ComposeSeed seed, CancellationToken ct) => Task.CompletedTask;

    public Task<bool> TryCloseAsync(CancellationToken ct) => Task.FromResult(true);
}

internal sealed class RecordingClip : Barbican.Presentation.Common.IClipboardService
{
    public string? Text { get; private set; }

    public void SetText(string text) => Text = text;
}

internal sealed class NoBooks : Barbican.Core.Contacts.IAddressBooks
{
    public Task<Barbican.Core.Common.Result<string>> EnsureDefaultAsync(string accountKey, CancellationToken ct) => Task.FromResult(Barbican.Core.Common.Result<string>.Ok("b1"));

    public Task<Barbican.Core.Common.Result<string>> CreateAsync(string accountKey, string name, CancellationToken ct) => Task.FromResult(Barbican.Core.Common.Result<string>.Ok("b9"));

    public Task<Barbican.Core.Common.Result<Barbican.Core.Common.Unit>> RenameAsync(string accountKey, string bookId, string name, CancellationToken ct) => Task.FromResult(Barbican.Core.Common.Result<Barbican.Core.Common.Unit>.Ok(default));

    public Task<Barbican.Core.Common.Result<Barbican.Core.Common.Unit>> DeleteAsync(string accountKey, string bookId, bool removeContacts, CancellationToken ct) => Task.FromResult(Barbican.Core.Common.Result<Barbican.Core.Common.Unit>.Ok(default));
}

internal sealed class ActionsHarness : IDisposable
{
    public ActionsHarness()
    {
        H = new ContactsHarness();
        Dialogs = new ScriptedDialogs();
        Composer = new RecordingComposer();
        Clip = new RecordingClip();
        Picker = new ContactsFilePicker();
        Actions = new ContactsActions(new ContactsActionDeps(H.View, H.Writer, new NoBooks(), Picker, new NoContactsEditors(), new NoContactsChrome(), H.Time)
        {
            Dialogs = Dialogs,
            Prefs = H.Prefs,
            Composer = Composer,
            Clipboard = Clip,
        });
    }

    public ContactsHarness H { get; }

    public ScriptedDialogs Dialogs { get; }

    public RecordingComposer Composer { get; }

    public RecordingClip Clip { get; }

    public ContactsFilePicker Picker { get; }

    public ContactsActions Actions { get; }

    public void Dispose() => H.Dispose();
}

public sealed class ContactsActionsTests
{
    [Fact]
    public async Task Delete_asks_first_and_does_nothing_when_the_answer_is_no()
    {
        using var h = new ActionsHarness();
        h.Dialogs.Answer(false);
        _ = await h.Actions.DeleteAsync("c1", CancellationToken.None);
        Assert.Empty(h.H.Writer.Deleted);
        var ask = Assert.IsType<ConfirmDialogRequest>(Assert.Single(h.Dialogs.Shown));
        Assert.True(ask.Danger);
        Assert.Contains("Ada Lovelace", ask.Message);
        h.Dialogs.Answer(true);
        _ = await h.Actions.DeleteAsync("c1", CancellationToken.None);
        Assert.Equal(["c1"], h.H.Writer.Deleted);
    }

    [Fact]
    public async Task Delete_without_an_argument_takes_the_selection()
    {
        using var h = new ActionsHarness();
        h.H.View.SelectContact("c1", false);
        h.H.View.SelectContact("c3", true);
        h.Dialogs.Answer(true);
        _ = await h.Actions.DeleteAsync(null, CancellationToken.None);
        Assert.Equal(["c1", "c3"], h.H.Writer.Deleted.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Group_toggle_adds_non_members_and_removes_when_everyone_is_in()
    {
        using var h = new ActionsHarness();
        _ = await h.Actions.ToggleGroupAsync(ContactsArg.Join("g1", "c2"), CancellationToken.None);
        var add = Assert.Single(h.H.Writer.Members);
        Assert.Equal("g1", add.Group);
        Assert.Equal(["urn:uuid:u-c2"], add.Add);
        Assert.Empty(add.Remove);
        h.H.Writer.Members.Clear();
        _ = await h.Actions.ToggleGroupAsync(ContactsArg.Join("g1", "c1", "c3"), CancellationToken.None);
        var remove = Assert.Single(h.H.Writer.Members);
        Assert.Empty(remove.Add);
        Assert.Equal(["urn:uuid:u-c1", "urn:uuid:u-c3"], remove.Remove);
    }

    [Fact]
    public async Task A_category_change_sends_the_whole_keywords_map()
    {
        using var h = new ActionsHarness();
        _ = await h.Actions.ToggleCategoryAsync(ContactsArg.Join("Work", "c1"), CancellationToken.None);
        var (id, patch) = Assert.Single(h.H.Writer.Updates);
        Assert.Equal("c1", id);
        Assert.Equal(["Friends", "Work"], ((JsonObject)patch["keywords"]!).Select(p => p.Key));
        h.H.Writer.Updates.Clear();
        _ = await h.Actions.ToggleCategoryAsync(ContactsArg.Join("Friends", "c1"), CancellationToken.None);
        Assert.Empty((JsonObject)h.H.Writer.Updates[0].Patch["keywords"]!);
    }

    [Fact]
    public async Task Deleting_a_category_asks_and_removes_it_from_every_contact()
    {
        using var h = new ActionsHarness();
        h.Dialogs.Answer(true);
        _ = await h.Actions.DeleteCategoryAsync("Friends", CancellationToken.None);
        Assert.Equal(["c1", "c3"], h.H.Writer.Updates.Select(u => u.Id).Order(StringComparer.Ordinal));
        Assert.All(h.H.Writer.Updates, u => Assert.Empty((JsonObject)u.Patch["keywords"]!));
    }

    [Fact]
    public async Task Send_email_opens_the_composer_with_the_addresses_and_a_group_sends_to_its_members()
    {
        using var h = new ActionsHarness();
        _ = await h.Actions.SendEmailAsync(ContactsArg.Join("c1", "c2"), CancellationToken.None);
        Assert.Equal("ada@example.org, emile@example.org", h.Composer.Opened[0].Prefill!.To);
        _ = await h.Actions.SendGroupAsync(ContactsArg.Join("bcc", "g1"), CancellationToken.None);
        Assert.Equal("ada@example.org, charles@example.org", h.Composer.Opened[1].Prefill!.Bcc);
        Assert.Equal(string.Empty, h.Composer.Opened[1].Prefill!.To);
    }

    [Fact]
    public async Task Copy_email_puts_the_first_address_on_the_clipboard()
    {
        using var h = new ActionsHarness();
        _ = await h.Actions.CopyEmailAsync("c3");
        Assert.Equal("charles@example.org", h.Clip.Text);
    }

    [Fact]
    public async Task Renaming_a_group_patches_the_whole_name_object()
    {
        using var h = new ActionsHarness();
        h.Dialogs.Answer("Analytical Engines");
        _ = await h.Actions.RenameGroupAsync("g1", CancellationToken.None);
        var (id, patch) = Assert.Single(h.H.Writer.Updates);
        Assert.Equal("g1", id);
        Assert.Equal("Analytical Engines", (string?)patch["name"]!["full"]);
    }

    [Fact]
    public void Export_names_are_safe_file_names()
    {
        Assert.Equal("contacts.vcf", ContactsActions.ExportName([ContactsHarness.Card("a", "A", "a@x.example", "b1"), ContactsHarness.Card("b", "B", "b@x.example", "b1")]));
        Assert.Equal("Ada_ Lovelace_.vcf", ContactsActions.ExportName([ContactsHarness.Card("a", "Ada: Lovelace?", "a@x.example", "b1")]));
        Assert.Equal("contacts.vcf", ContactsActions.ExportName([ContactsHarness.J("""{"id": "x"}""")]));
    }

    [Fact]
    public async Task Print_uses_the_selected_zone_for_birthday_tint()
    {
        using var h = new ActionsHarness();
        var card = h.H.Store.Cards.First(c => (string?)c["id"] == "c1");
        card["anniversaries"] = ContactsHarness.J("{\"b\":{\"kind\":\"birth\",\"date\":{\"month\":12,\"day\":3}}}");
        h.H.Store.Raise();
        h.H.Prefs.Set("{\"timeZone\":\"Pacific/Kiritimati\"}");
        _ = await h.Actions.PrintAsync("c1", TestContext.Current.CancellationToken);
        var print = Assert.IsType<ContactsPrintRequest>(Assert.Single(h.Dialogs.Shown));
        var line = Assert.Single(Assert.Single(Assert.Single(print.Document.Pages).Blocks).Lines, l => l.Section == "DATES");
        Assert.Equal("rose", line.Tint);
        Assert.Equal(3, (int?)card["anniversaries"]!["b"]!["date"]!["day"]);
    }

    [Fact]
    public void Print_pages_hold_whole_contacts()
    {
        var cards = Enumerable.Range(0, 20).Select(i => ContactsHarness.Card("c" + i, "Person " + i, "p" + i + "@x.example", "b1")).ToList();
        var document = ContactsPrintModel.Build(cards, new DateOnly(2026, 11, 18));
        Assert.Equal("Contacts", document.Title);
        Assert.True(document.Pages.Count > 1);
        Assert.Equal(20, document.Pages.Sum(p => p.Blocks.Count));
        Assert.Equal("Person 3", ContactsPrintModel.Build([cards[3]], new DateOnly(2026, 11, 18)).Title);
    }
}

public sealed class ContactsMenusAndCommandsTests
{
    private static MenuContext Context(string id, ContactMenuTarget target) => new(id, [], null, null, null, null, CommandSurface.ContextMenu) { Contact = target };

    private static string[] Labels(IEnumerable<MenuItemSpec> items) =>
        [.. items.Select(i => i.Kind switch { MenuItemKind.Separator => "-", MenuItemKind.Header => "# " + i.Label, MenuItemKind.Submenu => i.Label + " >", _ => i.Label })];

    [Fact]
    public void The_contact_menu_lists_the_spec_rows()
    {
        using var h = new ContactsHarness();
        var rows = ContactsMenus.Contact(Context(ContactsMenuIds.Contact, new ContactMenuTarget("acc", ["c1"])), h.View);
        Assert.Equal(["Open", "Send email", "Edit", "-", "Add to group >", "Categories >", "-", "Copy email address", "-", "Delete"], Labels(rows));
        Assert.True(rows[^1].Danger);
        var several = ContactsMenus.Contact(Context(ContactsMenuIds.Contact, new ContactMenuTarget("acc", ["c1", "c3"])), h.View);
        Assert.Equal(["Send email", "-", "Add to group >", "Categories >", "-", "Delete"], Labels(several));
        Assert.Empty(ContactsMenus.Contact(Context(ContactsMenuIds.Contact, new ContactMenuTarget("acc", [])), h.View));
    }

    [Fact]
    public void The_group_and_category_flyouts_check_what_every_contact_has()
    {
        using var h = new ContactsHarness();
        var groups = ContactsMenus.ContactGroups(Context(ContactsMenuIds.ContactGroups, new ContactMenuTarget("acc", ["c1", "c3"])), h.View);
        Assert.Equal(["Engines", "-", "New group…"], Labels(groups));
        Assert.True(groups[0].Checked);
        var single = ContactsMenus.ContactGroups(Context(ContactsMenuIds.ContactGroups, new ContactMenuTarget("acc", ["c2"])), h.View);
        Assert.False(single[0].Checked);
        var categories = ContactsMenus.ContactCategories(Context(ContactsMenuIds.ContactCategories, new ContactMenuTarget("acc", ["c1", "c2"])), h.View);
        Assert.Equal(["Friends", "-", "New category…"], Labels(categories));
        Assert.False(categories[0].Checked);
    }

    [Fact]
    public void The_group_category_and_book_menus_list_the_spec_rows()
    {
        using var h = new ContactsHarness();
        Assert.Equal(["# Engines", "Open", "Rename group", "Edit members", "Send email to group >", "-", "Delete group"], Labels(ContactsMenus.Group(Context(ContactsMenuIds.Group, new ContactMenuTarget("acc", [], GroupId: "g1")), h.View)));
        Assert.Equal(["To", "Cc", "Bcc"], Labels(ContactsMenus.GroupSend(Context(ContactsMenuIds.GroupSend, new ContactMenuTarget("acc", [], GroupId: "g1")))));
        Assert.Equal(["# Friends", "Show contacts", "Rename category", "-", "Delete category"], Labels(ContactsMenus.Category(Context(ContactsMenuIds.Category, new ContactMenuTarget("acc", [], Category: "Friends")))));
        var work = ContactsMenus.Book(Context(ContactsMenuIds.Book, new ContactMenuTarget("acc", [], BookId: "b2")), h.View);
        Assert.Equal(["# Work", "New contact in this address book", "Rename", "Set as default", "-", "Delete address book"], Labels(work));
        Assert.False(work[3].Enabled);
        Assert.True(work[^1].Enabled);
        var home = ContactsMenus.Book(Context(ContactsMenuIds.Book, new ContactMenuTarget("acc", [], BookId: "b1")), h.View);
        Assert.False(home[^1].Enabled);
    }

    [Fact]
    public void Every_command_has_a_unique_id_and_the_menus_name_only_registered_ones()
    {
        using var h = new ContactsHarness();
        var actions = new ContactsActions(new ContactsActionDeps(h.View, h.Writer, new NoBooks(), new ContactsFilePicker(), new NoContactsEditors(), new NoContactsChrome(), h.Time));
        var commands = ContactsCommands.Create(actions, h.View, new NoContactsChrome());
        Assert.Equal(ContactsCommands.Count, commands.Count);
        Assert.Equal(commands.Count, commands.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        var known = commands.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var target = new ContactMenuTarget("acc", ["c1"], GroupId: "g1", Category: "Friends", BookId: "b2");
        var items = ContactsMenus.Create(h.View).SelectMany(p => p.Build(Context(p.MenuId, target))).Where(i => i.CommandId is { Length: > 0 });
        Assert.All(items, i => Assert.Contains(i.CommandId!, known));
    }
}
