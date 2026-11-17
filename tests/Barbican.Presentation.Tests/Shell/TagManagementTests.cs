// New tests: tag management and the tag filter of the sidebar. Bulwark lib/keyword-nesting.ts (add, rename, name taken), components/layout/sidebar.tsx:1181 (a click selects, a second
// click on the selected tag clears it), :1190 (nested chevron); spec/menus.md MENU-TAG; spec I-055..I-058. Owner actions (m2-close-a).

#pragma warning disable MA0002, MA0006, VSTHRD003 // asserts compare ids ordinally by nature; fakes hand back completed tasks

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Mail.Model;
using Barbican.Core.Storage;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Tags;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Shell;

public sealed class TagManagementTests
{
    private const string Prefs = """{"emailKeywords": [{"id":"work","label":"Work","color":"green"},{"id":"home","label":"Home","color":"blue"}]}""";

    [Fact]
    public void A_new_tag_gets_an_id_from_its_name_and_the_settings_hold_it()
    {
        var (_, tags, editor) = Setup();
        var r = tags.Add(null, "Travel plans");
        Assert.True(r.IsOk);
        Assert.Equal("travel-plans", r.Value);
        Assert.Equal(["work", "home", "travel-plans"], tags.Defs.Select(d => d.Id));
        Assert.Equal(1, editor.Puts);
    }

    [Fact]
    public void A_name_that_is_taken_or_empty_is_refused_with_a_reason()
    {
        var (_, tags, editor) = Setup();
        Assert.Equal("A tag with that name is already there.", tags.Add(null, "work").Error!.Message);
        Assert.Equal("Give the tag a name.", tags.Add(null, "  ").Error!.Message);
        Assert.Equal(0, editor.Puts);
    }

    [Fact]
    public void Rename_keeps_the_id_and_recolour_takes_a_palette_name_only()
    {
        var (_, tags, _) = Setup();
        Assert.True(tags.Rename("work", "Job").IsOk);
        Assert.Equal("Job", tags.LabelOf("work"));
        Assert.True(tags.SetColour("work", "red").IsOk);
        Assert.Equal("red", tags.ColourOf("work"));
        Assert.False(tags.SetColour("work", "ultraviolet").IsOk);
        Assert.Equal("red", tags.ColourOf("work"));
    }

    [Fact]
    public void Delete_removes_the_definition_only_and_a_tag_with_tags_inside_stays()
    {
        var (_, tags, _) = Setup("""{"nestedTags": true, "emailKeywords": [{"id":"work","label":"Work","color":"green"},{"id":"work/clients","label":"Clients","color":"blue"}]}""");
        Assert.Equal("Delete the tags inside it first.", tags.Delete("work").Error!.Message);
        Assert.True(tags.Delete("work/clients").IsOk);
        Assert.True(tags.Delete("work").IsOk);
        Assert.Empty(tags.Defs);
    }

    [Fact]
    public async Task The_new_tag_command_asks_for_the_name_and_cancelling_does_nothing()
    {
        using var h = await Actions.ActionHarness.StartAsync(prefs: Prefs);
        var editor = new Editor(h.Prefs);
        var tags = new TagService(h.Prefs, editor);
        var dialogs = new Dialogs();
        var commands = TagCommands.Create(tags, h.Sidebar, h.Actions, h.Writers, h.Active, dialogs).ToDictionary(c => c.Id, StringComparer.Ordinal);

        dialogs.Answer = null;
        Assert.True((await commands[CommandIds.NewTag].ExecuteAsync(Ctx(null), CancellationToken.None)).IsOk);
        Assert.Equal(0, editor.Puts);

        dialogs.Answer = "Receipts";
        Assert.True((await commands[CommandIds.NewTag].ExecuteAsync(Ctx(null), CancellationToken.None)).IsOk);
        Assert.Contains(tags.Defs, d => string.Equals(d.Id, "receipts", StringComparison.Ordinal));
        Assert.Equal("New tag", dialogs.Asked.Last().Title);

        dialogs.Answer = "Work";
        Assert.False((await commands[CommandIds.NewTag].ExecuteAsync(Ctx(null), CancellationToken.None)).IsOk);
    }

    [Fact]
    public async Task A_click_filters_the_list_to_the_tag_and_a_second_click_goes_back_to_the_folder()
    {
        using var h = new ListHarness(Prefs);
        var queries = new List<ListQuery>();
        h.Service.OnQuery = (q, _, _) =>
        {
            queries.Add(q);
            return Task.FromResult(Result<ListPage>.Ok(Mails.Page(0, 2, 2, false)));
        };
        h.Sidebar.Update(new MailboxTree(
        [
            new Mailbox { Id = "inbox", Name = "INBOX", Role = MailboxRole.Inbox },
            new Mailbox { Id = "trash", Name = "Trash", Role = MailboxRole.Trash },
            new Mailbox { Id = "junk", Name = "Junk", Role = MailboxRole.Junk },
        ]));
        await h.Sidebar.SelectFolderAsync("inbox");

        await h.Sidebar.SelectTagAsync("work");
        Assert.Equal("work", h.Sidebar.SelectedTagId);
        Assert.Null(h.Sidebar.SelectedFolderId);
        Assert.Equal("Work", h.List.Title);
        Assert.True(h.Sidebar.Tags.Single(t => t.Id == "work").Selected);
        var tagQuery = queries.Last();
        Assert.Equal("$label:work", tagQuery.HasKeyword);
        Assert.Equal(["trash", "junk"], tagQuery.ExcludedMailboxes);

        await h.Sidebar.SelectTagAsync("work");
        Assert.Null(h.Sidebar.SelectedTagId);
        Assert.Equal("inbox", h.Sidebar.SelectedFolderId);
        Assert.Equal("inbox", h.List.MailboxId);
        Assert.DoesNotContain(h.Sidebar.Tags, t => t.Selected);
    }

    private static CommandContext Ctx(string? argument) => new(CommandSurface.ContextMenu, [], null, argument, "acct");

    private static (TestPrefs Prefs, TagService Tags, Editor Editor) Setup(string json = Prefs)
    {
        var prefs = new TestPrefs(json);
        var editor = new Editor(prefs);
        return (prefs, new TagService(prefs, editor), editor);
    }

    private sealed class Editor(TestPrefs prefs) : IPrefsEditor
    {
        public int Puts { get; private set; }

        public void Put(string key, JsonNode? value)
        {
            Puts++;
            var root = JsonNode.Parse(prefs.Current.Values.ToJsonString())!.AsObject();
            root[key] = value?.DeepClone();
            prefs.Set(root.ToJsonString());
        }

        public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Dialogs : IDialogService
    {
        public string? Answer { get; set; }

        public List<PromptDialogRequest> Asked { get; } = [];

        public bool IsOpen => false;

        public Task<TResult?> ShowAsync<TResult>(DialogRequest<TResult> request, CancellationToken ct)
        {
            Asked.Add((PromptDialogRequest)(object)request);
            return Task.FromResult((TResult?)(object?)Answer);
        }
    }

}
