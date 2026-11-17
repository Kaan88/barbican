using Barbican.Core.Editor;
using Barbican.Presentation.Editor;

namespace Barbican.Presentation.Tests.Editor;

/// <summary>I-268: Bulwark plain-text-paste.ts (a paragraph per line, blank lines kept, list markers recognised).</summary>
public sealed class PlainTextListPasteTests
{
    private static string Shape(IReadOnlyList<Block> blocks) => string.Join('|', blocks.Select(b => b.Kind switch
    {
        BlockKind.Bullet => $"{new string('>', b.Indent)}* {b.Text}",
        BlockKind.Ordered => $"{new string('>', b.Indent)}# {b.Text}",
        _ => $"p:{b.Text}",
    }));

    [Fact]
    public void Lines_and_blank_lines_become_paragraphs()
    {
        Assert.Equal("p:one|p:|p:two", Shape(PlainTextListPaste.Parse("one\n\ntwo")));
        Assert.Equal("p:a|p:b", Shape(PlainTextListPaste.Parse("a\r\nb")));
    }

    [Fact]
    public void Dashes_stars_bullets_and_numbers_become_lists()
    {
        Assert.Equal("* a|* b|* c", Shape(PlainTextListPaste.Parse("- a\n* b\n• c")));
        Assert.Equal("# x|# y", Shape(PlainTextListPaste.Parse("1. x\n2) y")));
        Assert.Equal("p:intro|* a|* b|p:", Shape(PlainTextListPaste.Parse("intro\n- a\n- b\n")));
    }

    [Fact]
    public void Indented_items_nest_and_blank_lines_between_items_are_dropped()
    {
        Assert.Equal("* a|>* a1|>* a2|* b", Shape(PlainTextListPaste.Parse("- a\n  - a1\n  - a2\n\n- b")));
    }

    [Fact]
    public void Wrapped_lines_continue_their_item_with_a_hard_break()
    {
        var blocks = PlainTextListPaste.Parse("- first part\nsecond part\n- next");
        Assert.Equal(2, blocks.Count);
        Assert.Equal("first part\nsecond part", blocks[0].Text);
    }

    [Fact]
    public void Text_that_only_looks_like_a_marker_stays_a_paragraph()
    {
        Assert.Equal("p:-not a list|p:1.5 is a number|p:-", Shape(PlainTextListPaste.Parse("-not a list\n1.5 is a number\n-")));
    }
}
