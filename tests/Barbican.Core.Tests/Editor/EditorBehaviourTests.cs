#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature
using Barbican.Core.Editor;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Editor;

/// <summary>Behaviour of <see cref="EditorSession"/> beyond the ported Rust tests: undo grouping by the injected clock, history bound, UTF-16 and grapheme steps, plain mode, links, lists, tables, events.</summary>
public sealed class EditorBehaviourTests
{
    private static EditorSession Ed(string text, FakeTimeProvider? time = null)
    {
        var e = new EditorSession(Doc.FromText(text), time ?? new FakeTimeProvider());
        e.SetCaret(e.Doc.End);
        return e;
    }

    private static List<string> Texts(EditorSession e) => [.. e.Doc.Blocks.Select(b => b.Text)];

    private static void Typ(EditorSession e, string s)
    {
        foreach (var rune in s.EnumerateRunes())
        {
            e.InsertText(rune.ToString());
        }
    }

    [Fact]
    public void Typing_merges_within_1200_ms_and_splits_after_the_pause()
    {
        var clock = new FakeTimeProvider();
        var e = Ed("", clock);
        Typ(e, "ab");
        clock.Advance(TimeSpan.FromMilliseconds(1199));
        Typ(e, "c");
        clock.Advance(TimeSpan.FromMilliseconds(1200));
        Typ(e, "d");
        Assert.Equal(["abcd"], Texts(e));
        Assert.True(e.Undo());
        Assert.Equal(["abc"], Texts(e));
        Assert.True(e.Undo());
        Assert.Equal([""], Texts(e));
        Assert.False(e.Undo());
    }

    [Fact]
    public void A_caret_move_ends_the_typing_group()
    {
        var e = Ed("");
        Typ(e, "ab");
        e.SetCaret(new Pos(0, 1));
        Typ(e, "X");
        Assert.Equal(["aXb"], Texts(e));
        e.Undo();
        Assert.Equal(["ab"], Texts(e));
    }

    [Fact]
    public void Deleting_groups_separately_from_typing()
    {
        var e = Ed("hello");
        e.Backspace(word: false);
        e.Backspace(word: false);
        Typ(e, "p");
        Assert.Equal(["help"], Texts(e));
        e.Undo();
        Assert.Equal(["hel"], Texts(e));
        e.Undo();
        Assert.Equal(["hello"], Texts(e));
    }

    [Fact]
    public void History_keeps_300_steps()
    {
        var e = Ed("");
        for (var i = 0; i < 350; i++)
        {
            e.Enter(shift: false);
        }

        var n = 0;
        while (e.Undo())
        {
            n++;
        }

        Assert.Equal(EditorSession.HistoryLimit, n);
        Assert.Equal(351 - 300, e.Doc.Blocks.Count);
    }

    [Fact]
    public void A_new_edit_clears_redo()
    {
        var e = Ed("a");
        Typ(e, "b");
        e.Undo();
        Assert.True(e.CanRedo);
        Typ(e, "c");
        Assert.False(e.CanRedo);
        Assert.False(e.Redo());
    }

    [Fact]
    public void Changed_is_raised_for_edits_selection_and_history_and_version_grows_on_edits()
    {
        var e = Ed("a");
        var n = 0;
        e.Changed += (_, _) => n++;
        var v = e.Version;
        e.SetCaret(new Pos(0, 0));
        Assert.Equal(1, n);
        Assert.Equal(v, e.Version);
        e.InsertText("x");
        Assert.True(n >= 2);
        Assert.True(e.Version > v);
        var before = n;
        e.Undo();
        Assert.True(n > before);
    }

    [Fact]
    public void Load_replaces_the_document_and_clears_history_but_keeps_plain()
    {
        var e = Ed("a");
        e.Plain = true;
        Typ(e, "b");
        e.Load(Doc.FromText("fresh"));
        Assert.Equal(["fresh"], Texts(e));
        Assert.False(e.CanUndo);
        Assert.True(e.Plain);
        Assert.Equal(new Pos(0, 0), e.Sel.Focus);
    }

    [Fact]
    public void Surrogate_pairs_are_never_split_by_typing_deleting_and_selecting()
    {
        var e = Ed("");
        Typ(e, "a\U0001F600b");
        Assert.Equal("a\U0001F600b", e.Doc.Blocks[0].Text);
        e.SetCaret(new Pos(0, 3)); // after the emoji
        e.Backspace(word: false);
        Assert.Equal("ab", e.Doc.Blocks[0].Text);

        e = Ed("");
        Typ(e, "a\U0001F600b");
        e.SetCaret(new Pos(0, 1));
        e.DeleteForward(word: false);
        Assert.Equal("ab", e.Doc.Blocks[0].Text);

        e = Ed("x\U0001F600y");
        e.SetCaret(new Pos(0, 2)); // inside the pair: clamped back
        Assert.Equal(1, e.Sel.Focus.O);
        e.SelectWord(new Pos(0, 2));
        Assert.Equal("x\U0001F600y".Length >= 4, e.Sel.End.O >= 1);
    }

    [Fact]
    public void Combining_marks_and_zwj_sequences_delete_as_one_grapheme()
    {
        var e = Ed("éx"); // e + combining acute, x
        e.SetCaret(new Pos(0, 2));
        e.Backspace(word: false);
        Assert.Equal(["x"], Texts(e));

        const string family = "\U0001F468‍\U0001F469‍\U0001F467";
        e = Ed(family + "z");
        e.SetCaret(new Pos(0, family.Length));
        e.Backspace(word: false);
        Assert.Equal(["z"], Texts(e));

        e = Ed(family + "z");
        e.SetCaret(new Pos(0, 0));
        e.DeleteForward(word: false);
        Assert.Equal(["z"], Texts(e));
    }

    [Fact]
    public void Word_deletion_and_word_selection()
    {
        var e = Ed("one two  three");
        e.Backspace(word: true);
        Assert.Equal(["one two  "], Texts(e));

        e = Ed("one two three");
        e.SetCaret(new Pos(0, 4));
        e.DeleteForward(word: true);
        Assert.Equal(["one three"], Texts(e));

        e = Ed("it's a well-known fact");
        e.SelectWord(new Pos(0, 8));
        Assert.Equal("well-known", e.SelectedText());
        e.SelectWord(new Pos(0, 2));
        Assert.Equal("it's", e.SelectedText());
    }

    [Fact]
    public void Select_all_and_selected_blocks()
    {
        var e = Ed("a\nb\nc");
        e.SelectAll();
        Assert.Equal("a\nb\nc", e.SelectedText());
        Assert.Equal(3, e.SelectedBlocks().Count);
        e.SetCaret(new Pos(0, 0));
        Assert.Equal("", e.SelectedText());
        Assert.Empty(e.SelectedBlocks());
        e.SelectBlock(1);
        Assert.Equal("b", e.SelectedText());
    }

    [Fact]
    public void Typing_over_a_selection_replaces_it()
    {
        var e = Ed("hello world");
        e.SelectRange(new Pos(0, 6), new Pos(0, 11));
        e.InsertText("there");
        Assert.Equal(["hello there"], Texts(e));
        e.Undo();
        Assert.Equal(["hello world"], Texts(e));
    }

    [Fact]
    public void Pasted_text_with_line_breaks_becomes_paragraphs_and_cr_is_normalised()
    {
        var e = Ed("");
        e.InsertText("a\r\nb\rc");
        Assert.Equal(["a", "b", "c"], Texts(e));
    }

    [Fact]
    public void Multi_line_text_stays_in_one_block_inside_code_and_cells()
    {
        var e = Ed("");
        e.InsertText("```");
        e.InsertText(" ");
        Assert.Equal(new BlockKind.CodeBlock(), e.Doc.Blocks[0].Kind);
        e.InsertText("x\ny");
        Assert.Equal(["x\ny"], Texts(e));
    }

    [Fact]
    public void Enter_in_code_block_leaves_after_an_empty_last_line()
    {
        var e = Ed("");
        e.SetBlock(new BlockKind.CodeBlock());
        Typ(e, "a");
        e.Enter(shift: false);
        Assert.Equal(["a\n"], Texts(e));
        e.Enter(shift: false);
        Assert.Equal(["a", ""], Texts(e));
        Assert.Equal(new BlockKind.CodeBlock(), e.Doc.Blocks[0].Kind);
        Assert.Equal(new BlockKind.Para(), e.Doc.Blocks[1].Kind);
    }

    [Fact]
    public void Enter_on_a_heading_end_gives_a_paragraph_and_in_the_middle_keeps_the_heading()
    {
        var e = Ed("");
        Typ(e, "## Title");
        Assert.Equal(new BlockKind.Heading(2), e.Doc.Blocks[0].Kind);
        e.Enter(shift: false);
        Assert.Equal(new BlockKind.Para(), e.Doc.Blocks[1].Kind);
        e.SetCaret(new Pos(0, 2));
        e.Enter(shift: false);
        Assert.Equal(new BlockKind.Heading(2), e.Doc.Blocks[1].Kind);
        Assert.Equal(["Ti", "tle", ""], Texts(e));
    }

    [Fact]
    public void Shift_enter_inserts_a_line_break_in_the_block()
    {
        var e = Ed("ab");
        e.SetCaret(new Pos(0, 1));
        e.Enter(shift: true);
        Assert.Equal(["a\nb"], Texts(e));
    }

    [Fact]
    public void Enter_carries_bold_to_the_next_line_and_empty_quote_item_leaves()
    {
        var e = Ed("");
        e.ToggleMark(Mark.Bold);
        Typ(e, "ab");
        e.Enter(shift: false);
        Typ(e, "cd");
        Assert.All(e.Doc.Blocks[1].Runs, r => Assert.True(r.Marks.Bold));

        e = Ed("");
        Typ(e, "> q");
        Assert.Equal(new BlockKind.Quote(), e.Doc.Blocks[0].Kind);
        e.Enter(shift: false);
        Assert.Equal(new BlockKind.Quote(), e.Doc.Blocks[1].Kind);
        e.Enter(shift: false);
        Assert.Equal(new BlockKind.Para(), e.Doc.Blocks[1].Kind);
    }

    [Fact]
    public void Block_rules_cover_every_markdown_prefix_and_do_not_fire_in_plain_mode()
    {
        foreach (var (typed, kind) in new (string, BlockKind)[]
        {
            ("# ", new BlockKind.Heading(1)), ("## ", new BlockKind.Heading(2)), ("### ", new BlockKind.Heading(3)),
            ("- ", new BlockKind.Bullet()), ("* ", new BlockKind.Bullet()), ("+ ", new BlockKind.Bullet()),
            ("> ", new BlockKind.Quote()), ("``` ", new BlockKind.CodeBlock()), ("12. ", new BlockKind.Ordered()),
        })
        {
            var e = Ed("");
            Typ(e, typed);
            Assert.Equal(kind, e.Doc.Blocks[0].Kind);
            Assert.Equal("", e.Doc.Blocks[0].Text);
        }

        var plain = Ed("");
        plain.Plain = true;
        Typ(plain, "# x **b**");
        Assert.Equal(["# x **b**"], Texts(plain));
        Assert.Equal(new BlockKind.Para(), plain.Doc.Blocks[0].Kind);
    }

    [Fact]
    public void Inline_rules_cover_strike_underscore_bold_and_skip_code_blocks_and_spaces()
    {
        var e = Ed("");
        Typ(e, "~~gone~~ __b__ ");
        Assert.Equal(["gone b "], Texts(e));
        Assert.Contains(e.Doc.Blocks[0].Runs, r => r.Text == "gone" && r.Marks.Strike);
        Assert.Contains(e.Doc.Blocks[0].Runs, r => r.Text == "b" && r.Marks.Bold);

        e = Ed("");
        Typ(e, "* not italic *");
        Assert.Equal(new BlockKind.Bullet(), e.Doc.Blocks[0].Kind);

        e = Ed("");
        e.SetBlock(new BlockKind.CodeBlock());
        Typ(e, "**x**");
        Assert.Equal(["**x**"], Texts(e));

        e = Ed("");
        Typ(e, "a ** b**");
        Assert.Equal(["a ** b**"], Texts(e));
    }

    [Fact]
    public void An_input_rule_is_its_own_undo_step()
    {
        var e = Ed("");
        Typ(e, "a **b**");
        Assert.Equal(["a b"], Texts(e));
        e.Undo();
        Assert.Equal(["a **b**"], Texts(e)); // the typed markers come back
    }

    [Fact]
    public void Set_block_toggles_back_to_paragraph_and_skips_atomic_blocks()
    {
        var e = Ed("a\nb");
        e.SelectAll();
        e.SetBlock(new BlockKind.Quote());
        Assert.All(e.Doc.Blocks, b => Assert.Equal(new BlockKind.Quote(), b.Kind));
        e.SetBlock(new BlockKind.Quote());
        Assert.All(e.Doc.Blocks, b => Assert.Equal(new BlockKind.Para(), b.Kind));

        e = Ed("a");
        e.InsertRule();
        e.SelectAll();
        e.SetBlock(new BlockKind.Heading(1));
        Assert.Contains(e.Doc.Blocks, b => b.Kind is BlockKind.Rule);
        Assert.Equal(new BlockKind.Heading(1), e.Doc.Blocks[0].Kind);
    }

    [Fact]
    public void Indent_is_one_level_deeper_than_the_previous_item_and_outdent_ends_the_list()
    {
        var e = Ed("");
        Typ(e, "- a");
        e.Enter(shift: false);
        Typ(e, "b");
        e.Enter(shift: false);
        Typ(e, "c");
        e.SelectAll();
        Assert.True(e.Indent(1));
        Assert.Equal([0, 1, 1], e.Doc.Blocks.Select(b => b.Indent).ToArray());
        Assert.True(e.Indent(1)); // c may now go one deeper than b
        Assert.Equal([0, 1, 2], e.Doc.Blocks.Select(b => b.Indent).ToArray());
        Assert.True(e.Indent(-1));
        Assert.Equal([0, 0, 1], e.Doc.Blocks.Select(b => b.Indent).ToArray());
        Assert.True(e.Indent(-1));
        Assert.Equal([0, 0, 0], e.Doc.Blocks.Select(b => b.Indent).ToArray());
        Assert.True(e.Indent(-1));
        Assert.All(e.Doc.Blocks, b => Assert.Equal(new BlockKind.Para(), b.Kind));
        Assert.False(e.Indent(1));
    }

    [Fact]
    public void Clear_format_resets_marks_keeps_links_and_turns_headings_into_paragraphs()
    {
        var e = Ed("");
        Typ(e, "# head");
        e.SelectAll();
        e.ToggleMark(Mark.Bold);
        e.SetColor("#ff0000");
        e.SetAlign(Align.Center);
        e.SetLink("x.example", "");
        e.ClearFormat();
        var b = e.Doc.Blocks[0];
        Assert.Equal(new BlockKind.Para(), b.Kind);
        Assert.Equal(Align.Left, b.Align);
        Assert.All(b.Runs, r =>
        {
            Assert.False(r.Marks.Bold);
            Assert.Null(r.Marks.Color);
            Assert.Equal("https://x.example", r.Marks.Link);
        });
    }

    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("http://a.example/x", "http://a.example/x")]
    [InlineData("me@x.example", "mailto:me@x.example")]
    [InlineData("mailto:me@x.example", "mailto:me@x.example")]
    [InlineData("tel:+4912", "tel:+4912")]
    [InlineData("a@b.example/path", "https://a@b.example/path")]
    [InlineData("  spaced.example  ", "https://spaced.example")]
    public void Links_are_normalised(string typed, string expected) => Assert.Equal(expected, EditorSession.NormalizeUrl(typed));

    [Fact]
    public void Set_link_with_a_collapsed_caret_inserts_text_and_edits_an_existing_link()
    {
        var e = Ed("x ");
        e.SetLink("example.com", "site");
        Assert.Equal(["x site"], Texts(e));
        Assert.Equal("https://example.com", e.Doc.Blocks[0].Runs[^1].Marks.Link);
        Typ(e, "!");
        Assert.Null(e.Doc.Blocks[0].Runs[^1].Marks.Link);

        e.SetCaret(new Pos(0, 4));
        e.SetLink("other.example", "renamed");
        Assert.Equal(["x renamed!"], Texts(e));
        Assert.Equal("https://other.example", e.CurrentLink());
        Assert.Equal((0, 2, 9, "https://other.example"), e.LinkRange());

        e = Ed("");
        e.SetLink("me@x.example", "");
        Assert.Equal(["me@x.example"], Texts(e));
        Assert.Equal("mailto:me@x.example", e.Doc.Blocks[0].Runs[0].Marks.Link);
    }

    [Fact]
    public void Set_link_does_nothing_for_an_empty_url_outside_a_link()
    {
        var e = Ed("abc");
        var v = e.Version;
        e.SetLink("", "text");
        Assert.Equal(["abc"], Texts(e));
        Assert.Equal(v, e.Version);
    }

    [Fact]
    public void Insert_blocks_pastes_a_fragment_and_a_single_block_inline()
    {
        var e = Ed("ab");
        e.SetCaret(new Pos(0, 1));
        e.InsertBlocks([Block.Para("X")]);
        Assert.Equal(["aXb"], Texts(e));
        e.InsertBlocks([Block.Para("1"), Block.Para("2")]);
        Assert.Equal(["aX1", "2b"], Texts(e));
        e.Undo();
        Assert.Equal(["aXb"], Texts(e));
    }

    [Fact]
    public void Insert_rule_splits_the_block_and_keeps_an_editable_end()
    {
        var e = Ed("ab");
        e.SetCaret(new Pos(0, 1));
        e.InsertRule();
        Assert.Equal(["a", "", "b"], Texts(e));
        Assert.IsType<BlockKind.Rule>(e.Doc.Blocks[1].Kind);
        Assert.Equal(new Pos(2, 0), e.Sel.Focus);
    }

    [Fact]
    public void Table_commands_cover_rows_columns_header_and_tab_navigation()
    {
        var e = Ed("");
        e.InsertTable(2, 2);
        Assert.True(e.Doc.Blocks[0].Kind is BlockKind.Cell { Header: true });
        Typ(e, "a");
        Assert.True(e.CellTab(back: false));
        Typ(e, "b");
        Assert.True(e.CellTab(back: false));
        Typ(e, "c");
        Assert.True(e.CellTab(back: false));
        Typ(e, "d");

        // Tab in the last cell adds a row and lands in its first cell
        Assert.True(e.CellTab(back: false));
        Assert.Equal(6, e.Doc.Blocks.Count(b => b.Kind.IsCell));
        Assert.Equal(4, e.Sel.Focus.B);
        Assert.True(e.CellTab(back: true));
        Assert.Equal("d", e.SelectedText());

        Assert.True(e.ApplyTable(TableOp.RowAbove));
        Assert.Equal(8, e.Doc.Blocks.Count(b => b.Kind.IsCell));
        Assert.True(e.ApplyTable(TableOp.ColLeft));
        Assert.Equal(12, e.Doc.Blocks.Count(b => b.Kind.IsCell));
        Assert.True(e.ApplyTable(TableOp.DelCol));
        Assert.Equal(8, e.Doc.Blocks.Count(b => b.Kind.IsCell));
        Assert.True(e.ApplyTable(TableOp.ToggleHeader));
        Assert.False(e.Doc.Blocks[0].Kind is BlockKind.Cell { Header: true });
        Assert.True(e.ApplyTable(TableOp.ToggleHeader));
        Assert.True(e.Doc.Blocks[0].Kind is BlockKind.Cell { Header: true });

        // every cell has a distinct (row, col) and the table is rectangular
        var cells = e.Doc.Blocks.Select(b => b.Kind).OfType<BlockKind.Cell>().ToList();
        Assert.Equal(cells.Count, cells.Select(c => (c.Row, c.Col)).Distinct().Count());
        Assert.Equal(4, cells.Max(c => c.Row) + 1);
        Assert.Equal(2, cells.Max(c => c.Col) + 1);
        Assert.False(e.Doc.Blocks[^1].Kind.IsCell);
    }

    [Fact]
    public void Table_ops_outside_a_table_return_false_and_Tab_falls_through()
    {
        var e = Ed("x");
        Assert.False(e.ApplyTable(TableOp.RowBelow));
        Assert.False(e.CellTab(back: false));
    }

    [Fact]
    public void Deleting_the_last_row_or_column_of_a_one_cell_table_removes_the_table()
    {
        var e = Ed("");
        e.InsertTable(1, 1);
        Assert.True(e.ApplyTable(TableOp.DelRow));
        Assert.DoesNotContain(e.Doc.Blocks, b => b.Kind.IsCell);
        Assert.NotEmpty(e.Doc.Blocks);

        e = Ed("");
        e.InsertTable(1, 1);
        Assert.True(e.ApplyTable(TableOp.DelCol));
        Assert.DoesNotContain(e.Doc.Blocks, b => b.Kind.IsCell);
    }

    [Fact]
    public void An_image_inserted_in_a_cell_goes_after_the_table()
    {
        var e = Ed("");
        e.InsertTable(1, 2);
        e.InsertImage(new ImageData { Src = "https://x.example/a.png" });
        var kinds = e.Doc.Blocks.Select(b => b.Kind.GetType().Name).ToList();
        Assert.Equal(["Cell", "Cell", "Image", "Para"], kinds);
    }

    [Fact]
    public void Image_upload_state_is_recorded_by_cid_and_listed()
    {
        var e = Ed("");
        e.InsertImage(new ImageData { Src = "cid:a@x", Cid = "a@x", Bytes = [1] });
        e.InsertImage(new ImageData { Src = "https://x.example/b.png" });
        Assert.Single(e.InlineImages);
        Assert.Null(e.InlineImages[0].BlobId);
        Assert.True(e.SetImageUploaded("a@x", "B1", "image/png", "a.png"));
        Assert.Equal("B1", e.InlineImages[0].BlobId);
        Assert.Equal("a.png", e.InlineImages[0].Name);
        Assert.False(e.SetImageUploaded("missing", "B", "m", "n"));
    }

    [Fact]
    public void Resize_without_undo_updates_in_place_and_toggle_quoted_flips_open()
    {
        var e = Ed("");
        e.InsertImage(new ImageData { Src = "https://x.example/a.png" });
        Assert.True(e.ResizeImage(0, 10, 5, undo: true));
        var v = e.Version;
        Assert.True(e.ResizeImage(0, 20, 10, undo: false));
        Assert.True(e.Version > v);
        Assert.False(e.ResizeImage(99, 1, 1, undo: true));

        e.Doc.Blocks.Add(new Block(new BlockKind.Quoted("<p>q</p>", "q", Open: false, Cite: true)));
        e.Doc.Fix();
        var qi = e.Doc.Blocks.ToList().FindIndex(b => b.Kind is BlockKind.Quoted);
        e.ToggleQuoted(qi);
        Assert.True(((BlockKind.Quoted)e.Doc.Blocks[qi].Kind).Open);
        e.ToggleQuoted(0); // not a quote: nothing happens
    }

    [Fact]
    public void Backspace_at_block_start_joins_and_after_a_table_only_removes_an_empty_paragraph()
    {
        var e = Ed("ab\ncd");
        e.SetCaret(new Pos(1, 0));
        e.Backspace(word: false);
        Assert.Equal(["abcd"], Texts(e));
        Assert.Equal(new Pos(0, 2), e.Sel.Focus);

        e = Ed("");
        e.InsertTable(1, 1);
        e.Doc.Blocks.Add(Block.Para("after"));
        e.SetCaret(new Pos(1, 0));
        e.Backspace(word: false);
        Assert.Equal(2, e.Doc.Blocks.Count(b => !b.Kind.IsCell) + 1);
        Assert.Contains(e.Doc.Blocks, b => b.Text == "after");
    }

    [Fact]
    public void Delete_forward_at_the_end_joins_the_next_block_and_removes_an_atomic_one()
    {
        var e = Ed("ab\ncd");
        e.SetCaret(new Pos(0, 2));
        e.DeleteForward(word: false);
        Assert.Equal(["abcd"], Texts(e));

        e = Ed("ab");
        e.InsertRule();
        e.SetCaret(new Pos(0, 2));
        Assert.Contains(e.Doc.Blocks, b => b.Kind is BlockKind.Rule);
        e.DeleteForward(word: false);
        Assert.DoesNotContain(e.Doc.Blocks, b => b.Kind is BlockKind.Rule);
    }

    [Fact]
    public void Toggle_mark_over_a_mixed_selection_turns_it_on_then_off()
    {
        var e = Ed("ab cd");
        e.SelectRange(new Pos(0, 0), new Pos(0, 2));
        e.ToggleMark(Mark.Italic);
        e.SelectRange(new Pos(0, 0), new Pos(0, 5));
        Assert.False(e.CurrentMarks().Italic);
        e.ToggleMark(Mark.Italic);
        Assert.All(e.Doc.Blocks[0].Runs, r => Assert.True(r.Marks.Italic));
        e.ToggleMark(Mark.Italic);
        Assert.All(e.Doc.Blocks[0].Runs, r => Assert.False(r.Marks.Italic));
    }

    [Fact]
    public void Colour_highlight_and_size_apply_to_the_selection_and_to_the_next_typing()
    {
        var e = Ed("abc");
        e.SelectRange(new Pos(0, 0), new Pos(0, 3));
        e.SetColor("#112233");
        e.SetHighlight("#ffff00");
        e.SetSize(20);
        Assert.Equal(new Marks { Color = "#112233", Highlight = "#ffff00", Size = 20 }, e.Doc.Blocks[0].Runs[0].Marks);
        e.SetCaret(new Pos(0, 3));
        e.SetSize(null);
        Typ(e, "d");
        Assert.Null(e.Doc.Blocks[0].Runs[^1].Marks.Size);
        Assert.Equal("#112233", e.Doc.Blocks[0].Runs[^1].Marks.Color);
    }

    [Fact]
    public void Plain_mode_ignores_formatting_commands()
    {
        var e = Ed("abc");
        e.Plain = true;
        e.SelectAll();
        e.ToggleMark(Mark.Bold);
        e.SetColor("#ff0000");
        e.SetBlock(new BlockKind.Heading(1));
        e.SetAlign(Align.Center);
        e.SetLink("x.example", "");
        e.InsertRule();
        e.InsertTable(2, 2);
        e.InsertImage(new ImageData { Src = "https://x.example/a.png" });
        Assert.False(e.Indent(1));
        e.ClearFormat();
        Assert.Single(e.Doc.Blocks);
        Assert.True(e.Doc.Blocks[0].Runs.All(r => r.Marks.IsPlain));
        Assert.Equal(new BlockKind.Para(), e.Doc.Blocks[0].Kind);
        Assert.False(e.CanUndo);
    }

    [Fact]
    public void Replace_doc_is_one_undo_step_and_clamps_the_selection()
    {
        var e = Ed("a long first line\nsecond");
        e.SetCaret(new Pos(1, 6));
        e.ReplaceDoc(Doc.FromText("x"), undo: true);
        Assert.Equal(["x"], Texts(e));
        Assert.Equal(new Pos(0, 1), e.Sel.Focus);
        e.Undo();
        Assert.Equal(["a long first line", "second"], Texts(e));
    }

    [Fact]
    public void Replace_range_swaps_a_misspelt_word_as_one_step()
    {
        var e = Ed("a mispelled word");
        e.ReplaceRange(new Pos(0, 2), new Pos(0, 11), "misspelled");
        Assert.Equal(["a misspelled word"], Texts(e));
        Assert.Equal(new Pos(0, 12), e.Sel.Focus);
        e.Undo();
        Assert.Equal(["a mispelled word"], Texts(e));
    }

    [Fact]
    public void Replace_text_in_part_replaces_the_selection_only()
    {
        var e = Ed("keep this and that");
        e.SelectRange(new Pos(0, 5), new Pos(0, 9));
        e.ReplaceText("these\nlines  ", whole: false);
        Assert.Equal(["keep these", "lines and that"], Texts(e));
    }

    [Fact]
    public void Shortcuts_run_commands_normalise_chords_and_are_swallowed_in_plain_mode()
    {
        var e = Ed("abc");
        e.SelectAll();
        Assert.True(EditorShortcuts.TryExecute(e, "Mod-b"));
        Assert.True(e.Doc.Blocks[0].Runs[0].Marks.Bold);
        Assert.True(EditorShortcuts.TryExecute(e, "MOD-B")); // case does not matter
        Assert.False(e.Doc.Blocks[0].Runs[0].Marks.Bold || e.Doc.Blocks[0].Runs[0].Marks.Italic);
        Assert.True(EditorShortcuts.TryExecute(e, "Mod-Alt-1"));
        Assert.Equal(new BlockKind.Heading(1), e.Doc.Blocks[0].Kind);
        Assert.True(EditorShortcuts.TryExecute(e, "Mod-Alt-0"));
        Assert.True(EditorShortcuts.TryExecute(e, "Mod-Shift-8"));
        Assert.Equal(new BlockKind.Bullet(), e.Doc.Blocks[0].Kind);
        Assert.True(EditorShortcuts.TryExecute(e, "Mod-Shift-e"));
        Assert.Equal(Align.Center, e.Doc.Blocks[0].Align);
        Assert.True(EditorShortcuts.TryExecute(e, "Mod-z"));
        Assert.True(EditorShortcuts.TryExecute(e, "Mod-y"));
        Assert.True(EditorShortcuts.TryExecute(e, "Mod-Shift-z"));
        Assert.False(EditorShortcuts.TryExecute(e, "Mod-q"));

        e.Plain = true;
        var before = e.Version;
        Assert.True(EditorShortcuts.TryExecute(e, "Mod-i")); // consumed, nothing happens
        Assert.Equal(before, e.Version);
        Assert.True(EditorShortcuts.TryExecute(e, "Enter"));
    }

    [Fact]
    public void Tab_moves_between_cells_or_indents_lists_and_otherwise_falls_through()
    {
        var e = Ed("plain");
        Assert.False(EditorShortcuts.TryExecute(e, "Tab"));

        e = Ed("");
        Typ(e, "- a");
        e.Enter(shift: false);
        Typ(e, "b");
        Assert.True(EditorShortcuts.TryExecute(e, "Tab"));
        Assert.Equal(1, e.Doc.Blocks[1].Indent);
        Assert.True(EditorShortcuts.TryExecute(e, "Shift-Tab"));
        Assert.Equal(0, e.Doc.Blocks[1].Indent);

        e = Ed("");
        e.InsertTable(1, 2);
        Assert.True(EditorShortcuts.TryExecute(e, "Tab"));
        Assert.Equal(1, e.Sel.Focus.B);
    }

    [Fact]
    public void Every_shortcut_chord_is_unique()
    {
        var chords = EditorShortcuts.All.Select(s => EditorShortcuts.Normalize(s.Chord)).ToList();
        Assert.Equal(chords.Count, chords.Distinct().Count());
        Assert.All(EditorShortcuts.All, s => Assert.Same(s, EditorShortcuts.Find(s.Chord)));
    }
}
