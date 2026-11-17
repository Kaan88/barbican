using Barbican.Core.Editor;
using Barbican.Presentation.Editor;

// Row labels and ids are compared as ordinal text in these tests; the comparer overloads add nothing here.
#pragma warning disable MA0006, MA0002

namespace Barbican.Presentation.Tests.Editor;

/// <summary>ToolbarState, command execution, the source view and spelling picks on a real session.</summary>
public sealed class EditorViewModelTests
{
    private static EditorViewModel Make(Doc? doc = null, bool plain = false, FakeSpellEngine? spell = null)
    {
        var session = new EditorSession(doc ?? new Doc([Block.Para("hello world")])) { Plain = plain };
        return new EditorViewModel(session, spell);
    }

    [Fact]
    public void Toolbar_follows_marks_block_align_and_selection()
    {
        var vm = Make();
        var changes = 0;
        vm.ToolbarChanged += (_, _) => changes++;
        vm.Session.SelectRange(new Pos(0, 0), new Pos(0, 5));
        vm.Execute(EditorCommand.Bold);
        vm.Execute(EditorCommand.Heading2);
        vm.Execute(EditorCommand.AlignCenter);
        vm.Execute(EditorCommand.Color, "#ff0000");
        vm.Execute(EditorCommand.Size, "20");
        var t = vm.Toolbar;
        Assert.True(t.Bold);
        Assert.False(t.Italic);
        Assert.Equal(new BlockKind.Heading(2), t.Block);
        Assert.Equal(Align.Center, t.Align);
        Assert.Equal("#ff0000", t.Color);
        Assert.Equal(20, t.Size);
        Assert.True(t.HasSelection);
        Assert.True(t.CanUndo);
        Assert.False(t.CanRedo);
        Assert.True(changes >= 3);
        vm.Session.Undo();
        Assert.True(vm.Toolbar.CanRedo);
    }

    [Fact]
    public void Toolbar_knows_tables_images_and_links()
    {
        var vm = Make();
        vm.Execute(EditorCommand.Table, "2x2");
        Assert.True(vm.Toolbar.InTable);
        Assert.IsType<BlockKind.Cell>(vm.Toolbar.Block);
        vm.Session.SetCaret(new Pos(0, 0));
        Assert.False(vm.Toolbar.InTable);
        vm.Session.InsertImage(new ImageData { Src = "https://example.test/a.png" });
        vm.Session.SetCaret(new Pos(vm.Session.Doc.Blocks.ToList().FindIndex(b => b.Kind is BlockKind.Image), 0));
        Assert.True(vm.Toolbar.OnImage);

        var l = Make();
        l.Session.SelectRange(new Pos(0, 0), new Pos(0, 5));
        l.Session.SetLink("example.test", string.Empty);
        l.Session.SetCaret(new Pos(0, 2));
        Assert.Equal("https://example.test", l.Toolbar.Link);
    }

    [Fact]
    public void Highlight_toggles_and_size_normal_clears_the_mark()
    {
        var vm = Make();
        vm.Session.SelectAll();
        vm.Execute(EditorCommand.Highlight);
        Assert.True(vm.Toolbar.Highlight);
        Assert.Equal(EditorCommandRunner.DefaultHighlight, vm.Session.Doc.Blocks[0].Runs[0].Marks.Highlight);
        vm.Execute(EditorCommand.Highlight);
        Assert.False(vm.Toolbar.Highlight);
        vm.Execute(EditorCommand.Size, "20");
        vm.Execute(EditorCommand.Size, "14");
        Assert.Null(vm.Toolbar.Size);
    }

    [Fact]
    public void Table_commands_edit_the_grid_and_the_registry_commands_follow_the_state()
    {
        var vm = Make();
        vm.Execute(EditorCommand.Table, "2x3");
        var cells = vm.Session.Doc.Blocks.Count(b => b.Kind.IsCell);
        Assert.Equal(6, cells);
        vm.Execute(EditorCommand.TableRowBelow);
        Assert.Equal(9, vm.Session.Doc.Blocks.Count(b => b.Kind.IsCell));
        vm.Execute(EditorCommand.TableColRight);
        Assert.Equal(12, vm.Session.Doc.Blocks.Count(b => b.Kind.IsCell));
        vm.Execute(EditorCommand.TableDelCol);
        vm.Execute(EditorCommand.TableDelRow);
        vm.Execute(EditorCommand.TableToggleHeader);
        vm.Execute(EditorCommand.TableDelete);
        Assert.DoesNotContain(vm.Session.Doc.Blocks, b => b.Kind.IsCell);
        Assert.False(EditorCommands.CanRun(vm, EditorCommand.TableDelRow));
        Assert.True(EditorCommands.CanRun(vm, EditorCommand.Bold));
        Assert.False(EditorCommands.CanRun(null, EditorCommand.Bold));
    }

    [Fact]
    public void Plain_mode_refuses_formatting_and_allows_editing_commands()
    {
        var vm = Make(plain: true);
        Assert.False(vm.Execute(EditorCommand.Bold));
        Assert.False(vm.Execute(EditorCommand.Link));
        Assert.True(vm.Execute(EditorCommand.SelectAll));
        Assert.False(EditorCommands.CanRun(vm, EditorCommand.Heading1));
        Assert.True(EditorCommands.CanRun(vm, EditorCommand.SelectAll));
    }

    [Fact]
    public void Switching_to_plain_text_drops_formatting()
    {
        var vm = Make();
        vm.Session.SelectAll();
        vm.Execute(EditorCommand.Bold);
        vm.Execute(EditorCommand.PlainMode);
        Assert.True(vm.Plain);
        Assert.All(vm.Session.Doc.Blocks.SelectMany(b => b.Runs), r => Assert.True(r.Marks.IsPlain));
        Assert.Equal("hello world", vm.Session.Doc.Blocks[0].Text);
    }

    [Fact]
    public void Commands_that_need_the_view_are_raised_as_requests()
    {
        var vm = Make();
        var seen = new List<EditorCommand>();
        vm.ViewCommandRequested += (_, e) => seen.Add(e.Command);
        Assert.True(vm.Execute(EditorCommand.Link));
        Assert.True(vm.Execute(EditorCommand.Paste));
        Assert.True(vm.Execute(EditorCommand.Image));
        Assert.Equal([EditorCommand.Link, EditorCommand.Paste, EditorCommand.Image], seen);
    }

    [Fact]
    public void Source_view_opens_with_the_html_applies_edits_live_as_one_undo_step_and_follows_outside_changes()
    {
        var vm = Make();
        var before = vm.Session.Doc.Blocks[0].Text;
        vm.ToggleSource();
        Assert.True(vm.SourceOpen);
        Assert.Contains("hello world", vm.SourceText, StringComparison.Ordinal);
        vm.EditSource("<p>one</p>");
        vm.EditSource("<p>one two</p>");
        Assert.Equal("one two", vm.Session.Doc.Blocks[0].Text);
        Assert.True(vm.Session.Undo());
        Assert.Equal(before, vm.Session.Doc.Blocks[0].Text);
        Assert.Contains("hello world", vm.SourceText, StringComparison.Ordinal);
        vm.ToggleSource();
        Assert.False(vm.SourceOpen);
        Assert.Equal(string.Empty, vm.SourceText);
    }

    [Fact]
    public void Misspelled_words_are_marked_except_the_one_being_typed_and_in_code_or_links()
    {
        var spell = new FakeSpellEngine();
        var vm = Make(new Doc([Block.Para("teh speling here")]), spell: spell);
        vm.Session.SetCaret(new Pos(0, 16));
        var marks = vm.Misspelled(0, "teh speling here");
        Assert.Equal([0, 4], marks.Select(m => m.Start));
        vm.Session.SetCaret(new Pos(0, 6));
        Assert.Equal([0], vm.Misspelled(0, "teh speling here").Select(m => m.Start));
        Assert.True(vm.Toolbar.OnMisspelledWord);
        vm.Session.SetCaret(new Pos(0, 12));
        Assert.False(vm.Toolbar.OnMisspelledWord);
        Assert.Null(vm.MisspelledAt(0, 13));
        Assert.Equal("speling", vm.MisspelledAt(0, 5)!.Text);
    }

    [Fact]
    public void Spelling_picks_replace_add_and_ignore_the_word_under_the_right_click()
    {
        var spell = new FakeSpellEngine();
        var vm = Make(new Doc([Block.Para("a speling b teh")]), spell: spell);
        vm.ContextWord = vm.MisspelledAt(0, 4);
        Assert.True(vm.Execute(EditorCommand.SpellReplace, "spelling"));
        Assert.Equal("a spelling b teh", vm.Session.Doc.Blocks[0].Text);
        Assert.Null(vm.ContextWord);
        vm.ContextWord = vm.MisspelledAt(0, 14);
        Assert.True(vm.Execute(EditorCommand.SpellAdd));
        Assert.Equal(["teh"], spell.Added);
        Assert.False(vm.Execute(EditorCommand.SpellIgnore));
    }
}
