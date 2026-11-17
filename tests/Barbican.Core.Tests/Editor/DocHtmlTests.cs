#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature (the ported Rust asserts)
using Barbican.Core.Editor;

namespace Barbican.Core.Tests.Editor;

/// <summary>The 12 tests of Rust src/rte/html.rs (its tests, quote_tests and source_tests modules), ported one to one (M3 stream editor-core).</summary>
public sealed class DocHtmlTests
{
    private static List<string> Texts(IEnumerable<Block> b) => [.. b.Select(x => x.Text)];

    [Fact]
    [RustTest("rte::html::import_inline_marks")]
    public void Import_inline_marks()
    {
        var b = DocHtml.Import("<p>Hello <b>bold</b> <i>it</i> <a href=\"https://x.y\">link</a> <span style=\"color: rgb(255,0,0); font-size:18px\">red</span></p>");
        Assert.Single(b);
        var r = b[0].Runs;
        Assert.Contains(r, x => x.Text == "bold" && x.Marks.Bold);
        Assert.Contains(r, x => x.Text == "it" && x.Marks.Italic);
        Assert.Contains(r, x => x.Text == "link" && x.Marks.Link == "https://x.y");
        Assert.Contains(r, x => x.Text == "red" && x.Marks.Color == "#ff0000" && x.Marks.Size == 18);
    }

    [Fact]
    [RustTest("rte::html::import_blocks_and_lists")]
    public void Import_blocks_and_lists()
    {
        var b = DocHtml.Import("<h1>T</h1><div>a<br>b</div><div><br></div><ul><li><p>one</p></li><li>two<ul><li>sub</li></ul></li></ul><ol><li>n</li></ol><blockquote><p>q</p></blockquote><pre>x\n  y</pre>");
        BlockKind[] expected =
        [
            new BlockKind.Heading(1), new BlockKind.Para(), new BlockKind.Para(), new BlockKind.Bullet(), new BlockKind.Bullet(), new BlockKind.Bullet(),
            new BlockKind.Ordered(), new BlockKind.Quote(), new BlockKind.CodeBlock(),
        ];
        Assert.Equal(expected, b.Select(x => x.Kind));
        Assert.Equal(["T", "a\nb", "", "one", "two", "sub", "n", "q", "x\n  y"], Texts(b));
        Assert.Equal(1, b[5].Indent);
    }

    [Fact]
    [RustTest("rte::html::import_table")]
    public void Import_table()
    {
        var b = DocHtml.Import("<table><tr><th>A</th><th>B</th></tr><tr><td>1</td></tr></table><p>after</p>");
        Assert.Equal(5, b.Count);
        Assert.True(b[0].Kind is BlockKind.Cell { Row: 0, Col: 0, Header: true });
        Assert.True(b[3].Kind is BlockKind.Cell { Row: 1, Col: 1 });
        Assert.Equal("after", b[4].Text);
    }

    [Fact]
    [RustTest("rte::html::import_whitespace_and_entities")]
    public void Import_whitespace_and_entities()
    {
        var b = DocHtml.Import("<div>\n   a  &amp;   b&nbsp;&nbsp;c\n</div>");
        Assert.Equal(["a & b  c"], Texts(b));
        b = DocHtml.Import("<style>p{}</style><!-- x --><p>ok</p>");
        Assert.Equal(["ok"], Texts(b));
    }

    [Fact]
    [RustTest("rte::html::round_trip")]
    public void Round_trip()
    {
        const string src = "<h2>Title</h2><div>Plain <strong>bold</strong> and <em>it</em></div><ul><li>a</li><li>b<ol><li>c</li></ol></li></ul><table><tr><td>x</td><td>y</td></tr></table><blockquote><div>quote</div></blockquote>";
        var d1 = new Doc(DocHtml.Import(src));
        var html = DocHtml.Export(d1);
        var d2 = new Doc(DocHtml.Import(html));
        Assert.Equal(d1.Blocks.Select(b => (b.Kind, b.Indent, b.Text)).ToList(), d2.Blocks.Select(b => (b.Kind, b.Indent, b.Text)).ToList());
        Assert.Contains("<strong>bold</strong> and <em>", html, StringComparison.Ordinal);
        Assert.Contains("<ol", html, StringComparison.Ordinal);
    }

    [Fact]
    [RustTest("rte::html::plain_text_output")]
    public void Plain_text_output()
    {
        var d = new Doc(DocHtml.Import("<p>Hi <a href=\"https://a.b\">site</a></p><ul><li>x</li></ul><ol><li>y</li><li>z</li></ol><blockquote>q</blockquote>"));
        Assert.Equal("Hi site <https://a.b>\n- x\n1. y\n2. z\n> q", DocHtml.ToText(d));
    }

    [Fact]
    [RustTest("rte::html::export_escapes")]
    public void Export_escapes()
    {
        var d = new Doc([Block.Para("a<b>&  c")]);
        var h = DocHtml.Export(d);
        Assert.Contains("a&lt;b&gt;&amp; &nbsp;c", h, StringComparison.Ordinal);
    }

    [Fact]
    [RustTest("rte::html::quoted_round_trip")]
    public void Quoted_round_trip()
    {
        var d = new Doc(
        [
            Block.Para("Thanks!"),
            Block.Para("On Mon, A wrote:"),
            new Block(new BlockKind.Quoted("<p>orig <b>x</b></p><blockquote>older</blockquote>", "orig x", Open: false, Cite: true)),
        ]);
        d.Fix();
        var h = DocHtml.Export(d);
        var b = DocHtml.ImportWithQuotes(h);
        Assert.Equal("Thanks!", b[0].Text);
        Assert.Equal("On Mon, A wrote:", b[1].Text);
        Assert.True(b[2].Kind is BlockKind.Quoted q && q.Html.Contains("<blockquote>older</blockquote>", StringComparison.Ordinal));
        Assert.Contains("> orig x", DocHtml.ToText(d), StringComparison.Ordinal);
    }

    [Fact]
    [RustTest("rte::html::clean_quote")]
    public void Clean_quote()
    {
        var h = DocHtml.CleanQuotedHtml("<!DOCTYPE html><html><head><style>p{color:red}</style></head><body><p>hi</p><script>x()</script></body></html>");
        Assert.Equal("<p>hi</p>", h);
    }

    [Fact]
    [RustTest("rte::html::source_round_trip")]
    public void Source_round_trip()
    {
        var bold = Block.Para("");
        bold.Runs.Add(new Run("Hello ", Marks.Plain));
        bold.Runs.Add(new Run("world", Marks.Plain with { Bold = true }));
        var d = new Doc(
        [
            bold,
            Block.WithText(new BlockKind.Heading(2), "Title", Marks.Plain),
            Block.WithText(new BlockKind.Bullet(), "one", Marks.Plain),
            Block.WithText(new BlockKind.Bullet(), "two", Marks.Plain),
            new Block(new BlockKind.Quoted("<p>orig</p>", "orig", Open: false, Cite: true)),
        ]);
        d.Fix();
        var s = DocHtml.Source(d);
        Assert.DoesNotContain("orig", s, StringComparison.Ordinal);
        Assert.True(s.Split('\n').Length >= 4, "one block per line: " + s);
        var back = DocHtml.ApplySource(d, s);
        Assert.Equal(["Hello world", "Title", "one", "two"], Texts(back.Blocks).Take(4));
        Assert.Contains(back.Blocks[0].Runs, r => r.Marks.Bold && r.Text == "world");
        Assert.Equal(new BlockKind.Heading(2), back.Blocks[1].Kind);
        Assert.Equal(1, back.Blocks.Count(b => b.Kind is BlockKind.Quoted));

        // edited source
        var e = DocHtml.ApplySource(d, "<p>New <i>text</i></p>\n<p>Second</p>");
        Assert.Equal("New text", e.Blocks[0].Text);
        Assert.Equal("Second", e.Blocks[1].Text);
        Assert.Equal(1, e.Blocks.Count(b => b.Kind is BlockKind.Quoted));
    }

    [Fact]
    [RustTest("rte::html::source_edit_keeps_inline_image_data")]
    public void Source_edit_keeps_inline_image_data()
    {
        var img = new ImageData
        {
            Src = "cid:img1@x",
            Cid = "img1@x",
            BlobId = "B123",
            Mime = "image/png",
            Name = "shot.png",
            Width = 100,
            Height = 50,
            Bytes = [1, 2, 3],
        };
        var d = new Doc([Block.Para("Hi"), new Block(new BlockKind.Image(img)), Block.Para("")]);
        d.Fix();
        var s = DocHtml.Source(d);
        Assert.Contains("cid:img1@x", s, StringComparison.Ordinal);
        var at = s.IndexOf("Hi", StringComparison.Ordinal);
        var e = DocHtml.ApplySource(d, string.Concat(s.AsSpan(0, at), "Hi there", s.AsSpan(at + 2)));
        var back = e.Blocks.Select(b => b.Kind).OfType<BlockKind.Image>().Single().Data;
        Assert.Equal("B123", back.BlobId);
        Assert.Equal("image/png", back.Mime);
        Assert.Equal("shot.png", back.Name);
        Assert.Equal(3, back.Bytes?.Length);
        Assert.Equal("Hi there", e.Blocks[0].Text);
    }

    [Fact]
    [RustTest("rte::html::image_width_round_trip")]
    public void Image_width_round_trip()
    {
        var img = new ImageData { Src = "https://x.org/a.png", Width = 240, Height = 120 };
        var d = new Doc([new Block(new BlockKind.Image(img))]);
        var h = DocHtml.Export(d);
        Assert.Contains("width=\"240\"", h, StringComparison.Ordinal);
        Assert.Contains("width:240px", h, StringComparison.Ordinal);
        var b = DocHtml.Import(h);
        Assert.Equal(240, Assert.IsType<BlockKind.Image>(b[0].Kind).Data.Width);

        // a CSS width alone is understood too
        b = DocHtml.Import("<img src=\"https://x.org/a.png\" style=\"width: 300px; height:auto\">");
        Assert.Equal(300, Assert.IsType<BlockKind.Image>(b[0].Kind).Data.Width);
    }
}
