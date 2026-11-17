using Barbican.Core.Editor;

namespace Barbican.Core.Tests.Editor;

/// <summary>
/// New tests for the UTF-16 offset decision (docs/adr/0006): caret steps are grapheme clusters and
/// positions never split a surrogate pair. Rust used UTF-8 bytes and code points, so these have no
/// Rust counterpart.
/// </summary>
public sealed class TextOffsetsTests
{
    // "a", U+1F44D U+1F3FD (thumbs up, skin tone: 4 code units), "e" + U+0301 (combining acute), "z"
    private const string Mixed = "a\U0001F44D\U0001F3FDéz";

    [Fact]
    public void Next_and_prev_step_over_whole_grapheme_clusters()
    {
        Assert.Equal(1, TextOffsets.NextChar(Mixed, 0));
        Assert.Equal(5, TextOffsets.NextChar(Mixed, 1));
        Assert.Equal(7, TextOffsets.NextChar(Mixed, 5));
        Assert.Equal(8, TextOffsets.NextChar(Mixed, 7));
        Assert.Equal(8, TextOffsets.NextChar(Mixed, 8));
        Assert.Equal(7, TextOffsets.PrevChar(Mixed, 8));
        Assert.Equal(5, TextOffsets.PrevChar(Mixed, 7));
        Assert.Equal(1, TextOffsets.PrevChar(Mixed, 5));
        Assert.Equal(0, TextOffsets.PrevChar(Mixed, 1));
        Assert.Equal(0, TextOffsets.PrevChar(Mixed, 0));
    }

    [Fact]
    public void Clamp_never_splits_a_surrogate_pair()
    {
        var d = Doc.FromText(Mixed);
        Assert.Equal(new Pos(0, 1), d.Clamp(new Pos(0, 2)));
        Assert.Equal(new Pos(0, 3), d.Clamp(new Pos(0, 3)));
        Assert.Equal(new Pos(0, 8), d.Clamp(new Pos(5, 99)));
    }

    [Fact]
    public void Insert_text_advances_by_utf16_length()
    {
        var d = Doc.FromText("ab");
        var p = d.InsertText(new Pos(0, 1), "\U0001F600", Marks.Plain);
        Assert.Equal(new Pos(0, 3), p);
        Assert.Equal("a\U0001F600b", d.Blocks[0].Text);
    }

    [Fact]
    public void Quoted_block_becomes_quote_lines_in_plain_text()
    {
        var d = new Doc([Block.Para("hi"), new Block(new BlockKind.Quoted("<p>x</p>", "x\ny\n", Open: false, Cite: true))]);
        d.Fix();
        Assert.Equal("hi\n> x\n> y\n", d.TextRange(new Pos(0, 0), d.End));
    }
}
