using Barbican.Core.Editor;

namespace Barbican.Core.Tests.Editor;

/// <summary>The 7 tests of Rust src/rte/model.rs, ported one to one (spike S3 slice).</summary>
public sealed class ModelTests
{
    private static readonly Marks Bold = Marks.Plain with { Bold = true };

    [Fact]
    [RustTest("rte::model::tests::insert_and_merge_runs")]
    public void Insert_and_merge_runs()
    {
        var b = Block.Para("hello world");
        b.MapMarks(0, 5, m => m with { Bold = true });
        Assert.Equal(2, b.Runs.Count);
        b.Insert(5, "!", Bold);
        Assert.Equal("hello!", b.Runs[0].Text);
        b.MapMarks(0, 6, m => m with { Bold = false });
        Assert.Single(b.Runs);
        Assert.Equal("hello! world", b.Text);
    }

    [Fact]
    [RustTest("rte::model::tests::split_and_merge_blocks")]
    public void Split_and_merge_blocks()
    {
        var d = Doc.FromText("abcdef");
        var p = d.Split(new Pos(0, 3));
        Assert.Equal(new Pos(1, 0), p);
        Assert.Equal("abc", d.Blocks[0].Text);
        Assert.Equal("def", d.Blocks[1].Text);
        p = d.Delete(new Pos(0, 2), new Pos(1, 1));
        Assert.Equal(new Pos(0, 2), p);
        Assert.Single(d.Blocks);
        Assert.Equal("abef", d.Blocks[0].Text);
    }

    [Fact]
    [RustTest("rte::model::tests::heading_enter_at_end_gives_paragraph")]
    public void Heading_enter_at_end_gives_paragraph()
    {
        var d = new Doc([Block.WithText(new BlockKind.Heading(1), "Title", Marks.Plain)]);
        var p = d.Split(new Pos(0, 5));
        Assert.Equal(new BlockKind.Para(), d.Blocks[p.B].Kind);
    }

    [Fact]
    [RustTest("rte::model::tests::ordinal_counts_levels")]
    public void Ordinal_counts_levels()
    {
        var d = new Doc([]);
        foreach (var lvl in new[] { 0, 0, 1, 0 })
        {
            var b = Block.Para("x");
            b.Kind = new BlockKind.Ordered();
            b.Indent = lvl;
            d.Blocks.Add(b);
        }

        Assert.Equal(2, d.Ordinal(1));
        Assert.Equal(1, d.Ordinal(2));
        Assert.Equal(3, d.Ordinal(3));
    }

    [Fact]
    [RustTest("rte::model::tests::paste_multi_block")]
    public void Paste_multi_block()
    {
        var d = Doc.FromText("helloworld");
        Block[] frag = [Block.Para("A"), Block.WithText(new BlockKind.Heading(2), "B", Marks.Plain), Block.Para("C")];
        var p = d.InsertBlocks(new Pos(0, 5), frag);
        Assert.Equal(["helloA", "B", "Cworld"], d.Blocks.Select(b => b.Text), StringComparer.Ordinal);
        Assert.Equal(new Pos(2, 1), p);
    }

    [Fact]
    [RustTest("rte::model::tests::delete_keeps_table_shape")]
    public void Delete_keeps_table_shape()
    {
        var d = new Doc([Block.Para("before")]);
        foreach (var (r, c) in new[] { (0, 0), (0, 1) })
        {
            d.Blocks.Add(Block.WithText(new BlockKind.Cell(0, r, c, Header: false), "cell", Marks.Plain));
        }

        d.Blocks.Add(Block.Para("after"));
        d.Delete(new Pos(1, 2), new Pos(2, 2));
        Assert.Equal(4, d.Blocks.Count);
        Assert.Equal("ce", d.Blocks[1].Text);
        Assert.Equal("ll", d.Blocks[2].Text);
    }

    [Fact]
    [RustTest("rte::model::tests::marks_at_caret")]
    public void Marks_at_caret()
    {
        var b = Block.Para("ab");
        b.MapMarks(0, 1, m => m with { Bold = true });
        Assert.True(b.MarksAt(1).Bold);
        Assert.False(b.MarksAt(2).Bold);
        Assert.True(b.MarksAt(0).Bold);
    }
}
