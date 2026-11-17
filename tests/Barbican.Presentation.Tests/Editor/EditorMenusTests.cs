using Barbican.Core.Editor;
using Barbican.Presentation.Editor;
using Barbican.Presentation.Menus;

// Row labels and ids are compared as ordinal text in these tests; the comparer overloads add nothing here.
#pragma warning disable MA0006, MA0002

namespace Barbican.Presentation.Tests.Editor;

/// <summary>spec/menus.md MENU-RTE item by item for each state: caret on a word, on a link, on an image, in a table, with a selection.</summary>
public sealed class EditorMenusTests
{
    private static EditorViewModel Make(Doc? doc = null, FakeSpellEngine? spell = null) =>
        new(new EditorSession(doc ?? new Doc([Block.Para("hello speling world")])), spell ?? new FakeSpellEngine());

    private static List<string> Labels(IReadOnlyList<MenuItemSpec> rows) =>
        [.. rows.Select(r => r.Kind == MenuItemKind.Separator ? "-" : r.Kind == MenuItemKind.Submenu ? r.Label + " >" : r.Label)];

    [Fact]
    public void Caret_in_plain_text_lists_the_base_rows_in_spec_order()
    {
        var vm = Make();
        var rows = EditorMenus.Main(vm);
        Assert.Equal(
            ["Cut", "Copy", "Paste", "Paste as plain text", "-", "Select all", "-", "Format >", "Paragraph >", "-", "Insert link…", "-", "Rewrite with AI >", "-", "Undo", "Redo"],
            Labels(rows));
        Assert.False(rows.Single(r => r.Label == "Cut").Enabled);
        Assert.False(rows.Single(r => r.Label == "Copy").Enabled);
        Assert.True(rows.Single(r => r.Label == "Paste").Enabled);
        Assert.False(rows.Single(r => r.Label == "Undo").Enabled);
        Assert.False(rows.Single(r => r.Label == "Rewrite with AI").Enabled);
        Assert.Equal("Ctrl+Shift+V", rows.Single(r => r.Label == "Paste as plain text").Hint);
        Assert.Equal("Ctrl+K", rows.Single(r => r.Label == "Insert link…").Hint);
        Assert.Equal("rte.paste-plain", rows.Single(r => r.Label == "Paste as plain text").CommandId);
    }

    [Fact]
    public void A_selection_enables_cut_and_copy_and_renames_the_assistant_row()
    {
        var vm = Make();
        vm.Session.SelectRange(new Pos(0, 0), new Pos(0, 5));
        var rows = EditorMenus.Main(vm);
        Assert.True(rows.Single(r => r.Label == "Cut").Enabled);
        Assert.True(rows.Single(r => r.Label == "Copy").Enabled);
        Assert.Contains(rows, r => r.Label == "Rewrite selection with AI");
    }

    [Fact]
    public void A_misspelt_word_adds_at_most_five_suggestions_add_and_ignore_first()
    {
        var vm = Make();
        vm.ContextWord = vm.MisspelledAt(0, 8);
        var rows = EditorMenus.Main(vm);
        Assert.Equal(["spelling", "spieling", "spewing", "spelunking", "spelling's", "Add \"speling\" to dictionary", "Ignore", "-"], Labels(rows).Take(8));
        Assert.Equal("spelling", rows[0].Argument);
        Assert.Equal("rte.spell-replace", rows[0].CommandId);
        Assert.Equal("rte.spell-add", rows[5].CommandId);
    }

    [Fact]
    public void A_misspelt_word_without_suggestions_says_so_in_a_disabled_row()
    {
        var vm = Make(new Doc([Block.Para("xyzzy")]));
        vm.ContextWord = vm.MisspelledAt(0, 2);
        var rows = EditorMenus.Main(vm);
        Assert.Equal("No suggestions", rows[0].Label);
        Assert.False(rows[0].Enabled);
    }

    [Fact]
    public void On_a_link_the_link_rows_replace_insert_link()
    {
        var vm = Make(new Doc([Block.Para("see here")]));
        vm.Session.SelectRange(new Pos(0, 4), new Pos(0, 8));
        vm.Session.SetLink("example.test", string.Empty);
        vm.Session.SetCaret(new Pos(0, 5));
        var labels = Labels(EditorMenus.Main(vm));
        Assert.Contains("Edit link…", labels);
        Assert.Contains("Open link", labels);
        Assert.Contains("Copy link address", labels);
        Assert.Contains("Remove link", labels);
        Assert.DoesNotContain("Insert link…", labels);
    }

    [Fact]
    public void On_an_image_insert_link_is_off_and_the_size_rows_appear()
    {
        var vm = Make(new Doc([Block.Para("x")]));
        vm.Session.InsertImage(new ImageData { Src = "https://example.test/a.png" });
        vm.Session.SetCaret(new Pos(vm.Session.Doc.Blocks.ToList().FindIndex(b => b.Kind is BlockKind.Image), 0));
        var rows = EditorMenus.Main(vm);
        Assert.False(rows.Single(r => r.Label == "Insert link…").Enabled);
        var sizes = rows.Where(r => r.CommandId == "rte.image-size").ToList();
        Assert.Equal(["Original image size", "Small (240 px)", "Medium (480 px)"], sizes.Select(r => r.Label));
        Assert.Equal(["0", "240", "480"], sizes.Select(r => r.Argument));
    }

    [Fact]
    public void The_table_flyout_row_appears_only_in_a_table_and_lists_its_rows()
    {
        var vm = Make();
        Assert.DoesNotContain("Table >", Labels(EditorMenus.Main(vm)));
        vm.Execute(EditorCommand.Table, "2x2");
        Assert.Contains("Table >", Labels(EditorMenus.Main(vm)));
        var rows = EditorMenus.TableRows(vm);
        Assert.Equal(
            ["Add row above", "Add row below", "Add column left", "Add column right", "-", "Delete row", "Delete column", "Toggle header row", "-", "Delete table"],
            Labels(rows));
        Assert.True(rows[^1].Danger);
    }

    [Fact]
    public void Format_and_paragraph_flyouts_check_the_current_state_and_show_shortcuts()
    {
        var vm = Make();
        vm.Execute(EditorCommand.Bold);
        vm.Execute(EditorCommand.NumberedList);
        vm.Execute(EditorCommand.AlignRight);
        var format = EditorMenus.FormatRows(vm);
        Assert.Equal(["Bold", "Italic", "Underline", "Strikethrough", "Inline code", "Highlight", "-", "Clear formatting"], Labels(format));
        Assert.True(format[0].Checked);
        Assert.False(format[1].Checked);
        Assert.Equal(["Ctrl+B", "Ctrl+I", "Ctrl+U", "Ctrl+Shift+S", "Ctrl+E", "Ctrl+Shift+H"], format.Take(6).Select(r => r.Hint));
        var para = EditorMenus.ParagraphRows(vm);
        Assert.Equal(
            ["Normal text", "Heading 1", "Heading 2", "Heading 3", "-", "Bulleted list", "Numbered list", "Quote", "Code block", "-", "Align left", "Center", "Align right", "Justify"],
            Labels(para));
        Assert.True(para.Single(r => r.Label == "Numbered list").Checked);
        Assert.True(para.Single(r => r.Label == "Align right").Checked);
        Assert.False(para.Single(r => r.Label == "Normal text").Checked);
        Assert.Equal("Ctrl+Alt+1", para.Single(r => r.Label == "Heading 1").Hint);
    }

    [Fact]
    public void Rewrite_flyout_lists_the_six_actions_disabled_until_the_assistant_exists()
    {
        var rows = EditorMenus.RewriteRows(Make());
        Assert.Equal(["Improve", "Shorten", "Formal", "Casual", "Fix spelling", "Translate"], rows.Select(r => r.Label));
        Assert.All(rows, r => Assert.False(r.Enabled));
    }

    [Fact]
    public void Plain_text_mode_disables_the_formatting_flyouts_and_link()
    {
        var vm = new EditorViewModel(new EditorSession(new Doc([Block.Para("x")])) { Plain = true });
        var rows = EditorMenus.Main(vm);
        Assert.False(rows.Single(r => r.Label == "Format").Enabled);
        Assert.False(rows.Single(r => r.Label == "Paragraph").Enabled);
        Assert.False(rows.Single(r => r.Label == "Insert link…").Enabled);
    }

    [Fact]
    public void Undo_and_redo_are_evaluated_when_the_menu_opens()
    {
        var vm = Make();
        vm.Session.SelectRange(new Pos(0, 0), new Pos(0, 5));
        vm.Execute(EditorCommand.Bold);
        Assert.True(EditorMenus.Main(vm).Single(r => r.Label == "Undo").Enabled);
        vm.Execute(EditorCommand.Undo);
        var rows = EditorMenus.Main(vm);
        Assert.False(rows.Single(r => r.Label == "Undo").Enabled);
        Assert.True(rows.Single(r => r.Label == "Redo").Enabled);
    }

    [Fact]
    public void Providers_and_commands_cover_every_row_id()
    {
        var active = new ActiveEditor();
        var providers = EditorMenus.Providers(active);
        Assert.Equal(EditorMenus.ProviderCount, providers.Count);
        Assert.Equal([EditorMenus.Menu, EditorMenus.Format, EditorMenus.Paragraph, EditorMenus.Table, EditorMenus.Rewrite], providers.Select(p => p.MenuId));
        var commands = EditorCommands.Create(active);
        Assert.Equal(Enum.GetValues<EditorCommand>().Length, commands.Count);
        var vm = Make();
        vm.Execute(EditorCommand.Table, "2x2");
        active.Activate(vm);
        var ids = commands.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            foreach (var row in provider.Build(new MenuContext(provider.MenuId, [], null, null, null, null, Barbican.Presentation.Commands.CommandSurface.ContextMenu)))
            {
                if (row.CommandId is not null && row.CommandId != "rte.rewrite")
                {
                    Assert.Contains(row.CommandId, ids);
                }
            }
        }

        Assert.Equal(EditorCommand.TableDelRow, EditorCommandIds.Parse("rte.table-del-row"));
        Assert.Equal("rte.paste-plain", EditorCommandIds.For(EditorCommand.PastePlain));
        active.Deactivate(vm);
        Assert.Null(active.Current);
    }
}
