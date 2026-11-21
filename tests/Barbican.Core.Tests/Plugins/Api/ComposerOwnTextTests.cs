// Source: lib/plugin-sandbox/composer-own-text.ts:44-80 (ownRange) and composer-edit.ts:38-137 (readOwnContent, replaceOwnContent keeps images and everything after the own text).
using Barbican.Core.Editor;
using Barbican.Core.Plugins.Api.Composer;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class ComposerOwnTextTests
{
    private static Block Context(string text)
    {
        var block = Block.Para(text);
        block.Runs[0] = block.Runs[0] with { IsComposerContext = true };
        return block;
    }

    private static Doc Reply() => new([Block.Para("Thanks!"), Block.Para("more"), Block.Para(""), Context("On Monday, Bob wrote:"),
        new Block(new BlockKind.Quoted("<p>Can you?</p>", "Can you?", false, true)), Block.Para(""), Context("-- Alice")]) { HasComposerProvenance = true };

    [Fact]
    public void OwnTextEndsAtTheReplyHeaderAndBlankLinesBeforeItStay()
    {
        var range = PluginComposerOwnText.Range(Reply());
        Assert.Equal(new PluginComposerOwnText.OwnRange(0, 2), range);
        var body = PluginComposerOwnText.Read(Reply());
        Assert.Equal("Thanks!\nmore", body.Text, StringComparer.Ordinal);
        Assert.Contains("Thanks!", body.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("wrote", body.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice", body.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void SignatureBelowTheTextIsABoundaryToo()
    {
        var doc = new Doc([Block.Para("Hello"), Block.Para(""), Context("Alice Example")]);
        Assert.Equal("Hello", PluginComposerOwnText.Read(doc).Text, StringComparer.Ordinal);
        Assert.Equal(new PluginComposerOwnText.OwnRange(0, 1), PluginComposerOwnText.Range(doc));
    }

    [Fact]
    public void AnEmptyDraftReplacesItsFirstEmptyParagraphOrInsertsAtTheBoundary()
    {
        Assert.Equal(new PluginComposerOwnText.OwnRange(0, 1), PluginComposerOwnText.Range(new Doc([Block.Para(""), Context("sig")])));
        Assert.Equal(new PluginComposerOwnText.OwnRange(0, 0), PluginComposerOwnText.Range(new Doc([Context("sig")])));
    }

    [Fact]
    public void ReplacingTheBodyKeepsTheSignatureTheQuoteAndInlineImages()
    {
        var picture = new Block(new BlockKind.Image(new ImageData { Cid = "pic@example.org" }));
        var doc = new Doc([Block.Para("old"), picture, Block.Para(""), Context("On Monday, Bob wrote:"),
            new Block(new BlockKind.Quoted("<p>q</p>", "q", false, true))]) { HasComposerProvenance = true };
        var replaced = PluginComposerOwnText.Replace(doc, [Block.Para("new one"), Block.Para("new two")]);
        Assert.Equal(["new one", "new two", "", "", "On Monday, Bob wrote:", "", ""], replaced.Blocks.Select(block => block.Text), StringComparer.Ordinal);
        Assert.IsType<BlockKind.Image>(replaced.Blocks[2].Kind);
        Assert.IsType<BlockKind.Quoted>(replaced.Blocks[^2].Kind);
        Assert.True(replaced.HasComposerProvenance);
        Assert.Equal("old", doc.Blocks[0].Text, StringComparer.Ordinal);
    }

    [Fact]
    public void ProtectedBlocksAreTheQuoteAndComposerGeneratedText()
    {
        Assert.True(PluginComposerOwnText.IsProtected(new Block(new BlockKind.Quoted("", "", false, true))));
        Assert.True(PluginComposerOwnText.IsProtected(Context("sig")));
        Assert.False(PluginComposerOwnText.IsProtected(Block.Para("typed")));
        Assert.False(PluginComposerOwnText.IsProtected(Block.Para("")));
    }
}
