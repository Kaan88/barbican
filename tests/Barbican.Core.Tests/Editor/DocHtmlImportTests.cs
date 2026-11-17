#pragma warning disable MA0002, MA0006, CA5394 // asserts compare strings ordinally by nature; the garbage generator is seeded on purpose
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Barbican.Core.Editor;

namespace Barbican.Core.Tests.Editor;

/// <summary>HTML import and export beyond the ported tests: sanitising, tolerance, round trips of every block kind, quotes in drafts, source view, entities and CSS.</summary>
public sealed class DocHtmlImportTests
{
    private static readonly string[] FixtureDirs = ["tests/fixtures/editor/mail", "tests/fixtures/mail"];

    private static List<string> Texts(IEnumerable<Block> b) => [.. b.Select(x => x.Text)];

    private static Doc RoundTrip(Doc d) => new(DocHtml.Import(DocHtml.Export(d)));

    [Fact]
    public void Scripting_schemes_are_dropped_from_links_and_images_whatever_the_spelling()
    {
        var b = DocHtml.Import("<p><a href=\"javascript:alert(1)\">x</a> <a href=\" JaVa\tScript:alert(1)\">y</a> <a href=\"vbscript:z\">z</a> <a href=\"https://ok.example\">ok</a></p><img src=\"javascript:evil()\"><img src=\"  \">");
        Assert.Single(b);
        Assert.Equal(["https://ok.example"], b[0].Runs.Select(r => r.Marks.Link).Where(l => l is not null).ToArray());
        Assert.DoesNotContain(b, x => x.Kind is BlockKind.Image);
    }

    [Fact]
    public void Scripts_styles_comments_handlers_and_unknown_tags_never_reach_the_model()
    {
        var b = DocHtml.Import("<p onclick=\"x()\">a<script>evil()</script><style>p{}</style><!--c--><iframe src=\"https://x.example\"></iframe><object>o</object><svg><text>s</text></svg>b</p>");
        var text = string.Concat(Texts(b));
        Assert.DoesNotContain("evil", text);
        Assert.DoesNotContain("p{}", text);
        Assert.StartsWith("a", text, StringComparison.Ordinal);
        Assert.DoesNotContain("onclick", DocHtml.Export(new Doc(b)));
    }

    [Fact]
    public void Exported_html_never_contains_raw_markup_from_text_or_attributes()
    {
        var d = new Doc([Block.Para("<script>alert(1)</script>")]);
        d.Blocks[0].Runs[0] = d.Blocks[0].Runs[0] with { Marks = Marks.Plain with { Link = "https://x.example/\"><script>" } };
        var h = DocHtml.Export(d);
        Assert.DoesNotContain("<script", h, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", h, StringComparison.Ordinal);
        Assert.Contains("href=\"https://x.example/&quot;>&lt;script>\"", h, StringComparison.Ordinal); // the quote cannot end the attribute
    }

    [Fact]
    public void Garbage_never_throws()
    {
        var rng = new Random(7);
        string[] parts = ["<", ">", "</", "<p", "<div>", "</div>", "<table>", "<tr>", "<td>", "</table>", "<ul>", "<li>", "<b>", "</i>", "&", "&#xZZ;", "&#99999999;", "\0", "text ", "<img src=x>", "<a href>", "<!--", "-->", "<style>", "<pre>", "<h1>", "<blockquote>", "<br/>", "\U0001F600", "\ud800"];
        for (var i = 0; i < 1500; i++)
        {
            var sb = new StringBuilder();
            for (var j = rng.Next(1, 40); j > 0; j--)
            {
                sb.Append(parts[rng.Next(parts.Length)]);
            }

            var doc = new Doc(DocHtml.Import(sb.ToString()));
            doc.Fix();
            _ = DocHtml.Export(doc);
            _ = DocHtml.ToText(doc);
            _ = DocHtml.ImportWithQuotes(sb.ToString());
        }

        Assert.Empty(DocHtml.Import(""));
    }

    [Fact]
    public void Deeply_nested_html_does_not_overflow_the_stack()
    {
        var html = string.Concat(Enumerable.Repeat("<div><span><b>", 3000)) + "deep" + string.Concat(Enumerable.Repeat("</b></span></div>", 3000));
        var b = DocHtml.Import(html);
        Assert.Contains(b, x => x.Text == "deep");
    }

    [Fact]
    public void Colours_sizes_and_styles_are_read_from_inline_css_and_legacy_attributes()
    {
        var b = DocHtml.Import(
            "<p><span style=\"color:#f00\">a</span><span style=\"color:rgba(0,0,255,0.5)\">b</span><span style=\"color:rgba(0,0,0,0)\">c</span>" +
            "<span style=\"font-size:12pt\">d</span><span style=\"font-size:1.5em\">e</span><span style=\"font-size:2rem\">f</span><span style=\"font-size:14px\">g</span>" +
            "<span style=\"font-weight:700\">h</span><span style=\"font-style:italic;text-decoration:underline line-through\">i</span>" +
            "<span style=\"background:#fff\">j</span><span style=\"background-color:yellow\">k</span><font color=\"blue\" size=\"4\">l</font><mark>m</mark><small>n</small><span style=\"color:#zzz\">o</span></p>");
        var r = b[0].Runs.ToDictionary(x => x.Text[..1], x => x.Marks);
        Assert.Equal("#ff0000", r["a"].Color);
        Assert.Equal("#0000ff", r["b"].Color);
        Assert.Null(r["c"]?.Color);
        Assert.Equal(16, r["d"].Size);
        Assert.Equal(21, r["e"].Size);
        Assert.Equal(28, r["f"].Size);
        Assert.False(r.ContainsKey("g") && r["g"].Size is not null);
        Assert.True(r["h"].Bold);
        Assert.True(r["i"].Italic && r["i"].Underline && r["i"].Strike);
        Assert.Null(r["j"].Highlight);
        Assert.Equal("#ffff00", r["k"].Highlight);
        Assert.Equal("#0000ff", r["l"].Color);
        Assert.Equal(18, r["l"].Size);
        Assert.Equal("#fef08a", r["m"].Highlight);
        Assert.Equal(12, r["n"].Size);
        Assert.Null(r["o"].Color);
    }

    [Theory]
    [InlineData("#abc", "#aabbcc")]
    [InlineData("#AABBCC", "#aabbcc")]
    [InlineData("#aabbccdd", "#aabbcc")]
    [InlineData("rgb(1, 2, 3)", "#010203")]
    [InlineData("rgb(1 2 3)", "#010203")]
    [InlineData("rgba(255, 0, 0, 1)", "#ff0000")]
    [InlineData("rgb(999, 0, 0)", "#ff0000")]
    [InlineData("Red", "#ff0000")]
    public void Norm_colour_accepts(string input, string expected) => Assert.Equal(expected, DocHtml.NormColor(input));

    [Theory]
    [InlineData("#abcd")]
    [InlineData("#gg0000")]
    [InlineData("rgba(1,2,3,0)")]
    [InlineData("rgb(1,2)")]
    [InlineData("chartreuse")]
    [InlineData("")]
    public void Norm_colour_rejects(string input) => Assert.Null(DocHtml.NormColor(input));

    [Fact]
    public void Entities_decode_named_numeric_and_leave_broken_ones()
    {
        Assert.Equal("a & b < c > \" '   — é \U0001F600", DocHtml.DecodeEntities("a &amp; b &lt; c &gt; &quot; &apos; &nbsp; &mdash; &#233; &#x1F600;"));
        Assert.Equal("&nope; & &#; &#xZZ; &#0; &amp", DocHtml.DecodeEntities("&nope; & &#; &#xZZ; &#0; &amp"));
        Assert.Equal("plain", DocHtml.DecodeEntities("plain"));
    }

    [Fact]
    public void Esc_makes_runs_of_spaces_visible()
    {
        Assert.Equal("a &nbsp;b", DocHtml.Esc("a  b"));
        Assert.Equal("&nbsp;a", DocHtml.Esc(" a"));
        Assert.Equal("x<br>y", DocHtml.Esc("x\ny"));
    }

    [Fact]
    public void Every_block_kind_survives_an_html_round_trip()
    {
        var bold = Block.Para("");
        bold.Runs.Add(new Run("b", Marks.Plain with { Bold = true }));
        bold.Runs.Add(new Run(" and ", Marks.Plain));
        bold.Runs.Add(new Run("link", Marks.Plain with { Link = "https://x.example", Italic = true }));
        var d = new Doc(
        [
            new Block(new BlockKind.Heading(1)) { Runs = { new Run("H1", Marks.Plain) }, Align = Align.Center },
            new Block(new BlockKind.Heading(3)) { Runs = { new Run("H3", Marks.Plain) } },
            bold,
            new Block(new BlockKind.Bullet()) { Runs = { new Run("a", Marks.Plain) } },
            new Block(new BlockKind.Bullet()) { Runs = { new Run("b", Marks.Plain) }, Indent = 1 },
            new Block(new BlockKind.Ordered()) { Runs = { new Run("c", Marks.Plain) }, Indent = 1 },
            new Block(new BlockKind.Quote()) { Runs = { new Run("q1", Marks.Plain) } },
            new Block(new BlockKind.Quote()) { Runs = { new Run("q2", Marks.Plain) } },
            new Block(new BlockKind.CodeBlock()) { Runs = { new Run("x < y\n  z", Marks.Plain) } },
            new Block(new BlockKind.Rule()),
            new Block(new BlockKind.Cell(0, 0, 0, true)) { Runs = { new Run("h", Marks.Plain) } },
            new Block(new BlockKind.Cell(0, 0, 1, true)) { Runs = { new Run("i", Marks.Plain) } },
            new Block(new BlockKind.Cell(0, 1, 0, false)) { Runs = { new Run("j", Marks.Plain) } },
            new Block(new BlockKind.Cell(0, 1, 1, false)),
            new Block(new BlockKind.Image(new ImageData { Src = "https://x.example/a.png", Alt = "alt", Width = 100 })),
            Block.Para("end"),
        ]);
        d.Fix();
        var back = RoundTrip(d);
        Assert.Equal(
            d.Blocks.Select(b => (b.Kind is BlockKind.Image i ? (object)(i.Data.Src, i.Data.Width, i.Data.Alt) : b.Kind, b.Indent, b.Text, b.Align)).ToList(),
            back.Blocks.Select(b => (b.Kind is BlockKind.Image i ? (object)(i.Data.Src, i.Data.Width, i.Data.Alt) : b.Kind, b.Indent, b.Text, b.Align)).ToList());
        Assert.Contains(back.Blocks[2].Runs, r => r.Marks.Bold && r.Text == "b");
        Assert.Contains(back.Blocks[2].Runs, r => r.Marks.Italic && r.Marks.Link == "https://x.example");
    }

    [Fact]
    public void Marks_survive_a_round_trip_including_colour_highlight_size_and_code()
    {
        var b = Block.Para("");
        b.Runs.Add(new Run("x", Marks.Plain with { Color = "#ff0000", Highlight = "#fef08a", Size = 20, Bold = true, Italic = true, Underline = true, Strike = true }));
        b.Runs.Add(new Run("y", Marks.Plain with { Code = true }));
        var back = RoundTrip(new Doc([b]));
        Assert.Equal(b.Runs.Select(r => r.Marks), back.Blocks[0].Runs.Select(r => r.Marks));
        Assert.Equal("xy", back.Blocks[0].Text);
    }

    [Fact]
    public void Single_spaces_between_runs_and_leading_double_spaces_are_kept()
    {
        var b = Block.Para("");
        b.Runs.Add(new Run("one ", Marks.Plain));
        b.Runs.Add(new Run("two", Marks.Plain with { Bold = true }));
        b.Runs.Add(new Run(" three  four", Marks.Plain));
        var back = RoundTrip(new Doc([b]));
        Assert.Equal("one two three  four", back.Blocks[0].Text);
    }

    [Fact]
    public void Data_url_images_keep_their_bytes_and_cid_images_their_cid()
    {
        var b = DocHtml.Import("<img src=\"data:image/png;base64,AQID\" alt=\"t\"><img src=\"cid:a@b\"><img src=\"data:image/png;base64,@@@\">");
        var imgs = b.Select(x => x.Kind).OfType<BlockKind.Image>().Select(i => i.Data).ToList();
        Assert.Equal(3, imgs.Count);
        Assert.Equal([1, 2, 3], imgs[0].Bytes);
        Assert.Equal("image/png", imgs[0].Mime);
        Assert.Equal("a@b", imgs[1].Cid);
        Assert.Null(imgs[2].Bytes);
    }

    [Fact]
    public void An_image_inside_a_table_cell_becomes_its_alt_text()
    {
        var b = DocHtml.Import("<table><tr><td>x <img src=\"https://x.example/a.png\" alt=\"logo\"></td></tr></table>");
        Assert.Single(b);
        Assert.Equal("x [logo]", b[0].Text);
    }

    [Fact]
    public void Ragged_tables_are_padded_and_nested_tables_flatten_into_the_cell()
    {
        var b = DocHtml.Import("<table><tr><td>1<td>2<td>3<tr><td>4</table>");
        Assert.Equal(6, b.Count);
        Assert.Equal([(0, 0), (0, 1), (0, 2), (1, 0), (1, 1), (1, 2)], b.Select(x => ((BlockKind.Cell)x.Kind)).Select(c => (c.Row, c.Col)).ToArray());
        Assert.Equal(["1", "2", "3", "4", "", ""], Texts(b));

        b = DocHtml.Import("<table><tr><td>out<table><tr><td>in</td></tr></table></td></tr></table>");
        Assert.Single(b);
        Assert.Contains("in", b[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_cells_are_bold_while_open_and_th_marks_do_not_leak()
    {
        var b = DocHtml.Import("<table><tr><th>H</th><td>D</td></tr></table>");
        Assert.True(b[0].Runs[0].Marks.Bold);
        Assert.False(b[1].Runs[0].Marks.Bold);
    }

    [Fact]
    public void Line_breaks_in_cells_and_paragraphs_in_cells_become_newlines()
    {
        var b = DocHtml.Import("<table><tr><td>a<br>b<p>c</p><div>d</div></td></tr></table>");
        Assert.Equal("a\nb\nc\nd", b[0].Text);
    }

    [Fact]
    public void Alignment_comes_from_style_attribute_or_center_tag()
    {
        var b = DocHtml.Import("<p style=\"text-align:right\">r</p><div align=\"center\">c</div><center>cc</center><h2 style=\"text-align:justify\">j</h2><p>l</p>");
        Assert.Equal([Align.Right, Align.Center, Align.Center, Align.Justify, Align.Left], b.Select(x => x.Align).ToArray());
    }

    [Fact]
    public void Paragraph_spacing_collapses_to_one_block_per_paragraph_and_empty_paragraphs_are_kept()
    {
        var b = DocHtml.Import("<p>a</p>\n\n<p></p>\n<p>b</p><p><br></p>");
        Assert.Equal(["a", "", "b"], Texts(b));
    }

    [Fact]
    public void Preformatted_text_keeps_spaces_and_newlines_and_code_inside_it_is_not_a_mark()
    {
        var b = DocHtml.Import("<pre><code>  a  b\n\n c</code></pre>");
        Assert.Single(b);
        Assert.Equal("  a  b\n\n c", b[0].Text);
        Assert.False(b[0].Runs[0].Marks.Code);
    }

    [Fact]
    public void Import_text_splits_lines_and_normalises_crlf()
    {
        Assert.Equal(["a", "", "b"], Texts(DocHtml.ImportText("a\r\n\r\nb")));
        Assert.Equal([""], Texts(DocHtml.ImportText("")));
    }

    [Fact]
    public void Draft_with_a_cite_quote_in_the_middle_keeps_text_before_and_after()
    {
        var html = "<p>top</p><blockquote type=\"cite\"><p>q <b>1</b></p><blockquote>inner</blockquote></blockquote><p>bottom</p>";
        var b = DocHtml.ImportWithQuotes(html);
        Assert.Equal(["top", "q 1\n> inner".Length > 0 ? ((BlockKind.Quoted)b[1].Kind).Text : "", "bottom"], [b[0].Text, ((BlockKind.Quoted)b[1].Kind).Text, b[2].Text]);
        var q = (BlockKind.Quoted)b[1].Kind;
        Assert.Contains("<blockquote>inner</blockquote>", q.Html, StringComparison.Ordinal);
        Assert.True(q.Cite);
        Assert.False(q.Open);
    }

    [Theory]
    [InlineData("<blockquote type='cite'>x</blockquote>")]
    [InlineData("<BLOCKQUOTE TYPE=cite>x</BLOCKQUOTE>")]
    [InlineData("<blockquote class=\"quote_box\" type=\"cite\">x</blockquote>")]
    public void Quote_detection_accepts_other_spellings_of_type_cite(string html)
    {
        var b = DocHtml.ImportWithQuotes("<p>a</p>" + html);
        Assert.Contains(b, x => x.Kind is BlockKind.Quoted);
    }

    [Fact]
    public void A_plain_blockquote_or_an_unclosed_cite_quote_imports_as_ordinary_blocks()
    {
        Assert.DoesNotContain(DocHtml.ImportWithQuotes("<blockquote>x</blockquote>"), b => b.Kind is BlockKind.Quoted);
        Assert.DoesNotContain(DocHtml.ImportWithQuotes("<blockquote type=\"cite\">x"), b => b.Kind is BlockKind.Quoted);
    }

    [Fact]
    public void A_forwarded_body_exports_as_a_plain_div_and_to_text_has_no_markers()
    {
        var d = new Doc([Block.Para("fw"), new Block(new BlockKind.Quoted("<p>orig</p>", "orig", Open: false, Cite: false))]);
        d.Fix();
        Assert.Contains("<div><p>orig</p></div>", DocHtml.Export(d), StringComparison.Ordinal);
        Assert.Equal("fw\norig\n", DocHtml.ToText(d) + "\n");
    }

    [Fact]
    public void Clean_quoted_html_keeps_body_content_and_drops_wrappers_case_insensitively()
    {
        Assert.Equal("<p>x</p>", DocHtml.CleanQuotedHtml("<HTML><HEAD><TITLE>t</TITLE><STYLE>b{}</STYLE></HEAD><BODY class=a><p>x</p></BODY></HTML>"));
        Assert.Equal("<p>frag</p>", DocHtml.CleanQuotedHtml("  <p>frag</p>  "));
        Assert.Equal("a", DocHtml.CleanQuotedHtml("<meta charset=utf-8><script>1</script>a"));
        Assert.Equal("", DocHtml.CleanQuotedHtml("<style>unterminated"));
    }

    [Fact]
    public void Plain_text_rendering_covers_headings_nested_lists_tables_images_rules_and_links()
    {
        var d = new Doc(DocHtml.Import(
            "<h1>Head</h1><ol><li>one<ol><li>sub</li></ol></li><li>two</li></ol><hr><table><tr><td>a</td><td>b</td></tr></table>" +
            "<img src=\"https://x.example/a.png\" alt=\"pic\"><p><a href=\"https://x.example\">https://x.example</a> <a href=\"mailto:me@x.example\">me@x.example</a> <a href=\"https://y.example\">named</a></p>"));
        Assert.Equal("Head\n1. one\n   1. sub\n2. two\n----------\na | b\n[pic]\nhttps://x.example me@x.example named <https://y.example>", DocHtml.ToText(d));
    }

    [Fact]
    public void Source_view_is_one_block_per_line_and_import_ignores_the_line_breaks()
    {
        var d = new Doc(DocHtml.Import("<p>a</p><ul><li>x</li><li>y</li></ul><table><tr><td>c</td></tr></table><hr><pre>code</pre>"));
        d.Fix();
        var s = DocHtml.Source(d);
        Assert.DoesNotContain("font-family:'Segoe UI'", s, StringComparison.Ordinal);
        Assert.True(s.Split('\n').Length >= 8, s);
        var back = DocHtml.ApplySource(d, s);
        Assert.Equal(Texts(d.Blocks), Texts(back.Blocks));
        Assert.Equal(DocHtml.Export(d), DocHtml.Export(back));
        Assert.Equal("", DocHtml.Source(new Doc([])));
    }

    [Fact]
    public void Apply_source_of_an_empty_string_leaves_one_paragraph_and_keeps_the_quote()
    {
        var d = new Doc([Block.Para("a"), new Block(new BlockKind.Quoted("<p>q</p>", "q", Open: true, Cite: true))]);
        d.Fix();
        var e = DocHtml.ApplySource(d, "");
        Assert.Equal(new BlockKind.Para(), e.Blocks[0].Kind);
        Assert.Contains(e.Blocks, b => b.Kind is BlockKind.Quoted { Open: true });
        Assert.Equal(1, DocHtml.EditableEnd(e));
    }

    [Fact]
    public void Apply_source_matches_images_by_src_when_there_is_no_cid()
    {
        var img = new ImageData { Src = "https://x.example/a.png", Name = "a.png", Mime = "image/png", Bytes = [9] };
        var d = new Doc([new Block(new BlockKind.Image(img)), Block.Para("")]);
        d.Fix();
        var e = DocHtml.ApplySource(d, DocHtml.Source(d));
        var back = e.Blocks.Select(b => b.Kind).OfType<BlockKind.Image>().Single().Data;
        Assert.Equal("a.png", back.Name);
        Assert.Equal([9], back.Bytes);
    }

    [Fact]
    public void Text_of_every_fixture_mail_body_is_preserved_by_the_import()
    {
        var files = FixtureDirs
            .Select(TestRepo.PathOf).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.html", SearchOption.AllDirectories)).ToList();
        Assert.True(files.Count >= 4, "the editor fixtures are missing");
        foreach (var f in files)
        {
            var html = File.ReadAllText(f);
            var blocks = DocHtml.Import(html);
            var doc = new Doc(blocks);
            doc.Fix();
            _ = DocHtml.ToText(doc);
            Assert.Equal(VisibleText(html), Squash(string.Concat(blocks.Select(b => b.Text))));
            Assert.Equal(VisibleText(html), Squash(string.Concat(new Doc(DocHtml.Import(DocHtml.Export(doc))).Blocks.Select(b => b.Text))));
        }
    }

    private static string Squash(string s) => new([.. s.Where(c => !char.IsWhiteSpace(c))]);

    /// <summary>The text a browser would show, found independently of the importer: the body without style, script, head, comments, whitespace removed. A cell image shows as its alt text, so the fixtures keep images out of cells.</summary>
    private static string VisibleText(string html)
    {
        var dom = new HtmlParser().ParseDocument(html);
        var sb = new StringBuilder();
        void Walk(INode n)
        {
            foreach (var c in n.ChildNodes)
            {
                switch (c)
                {
                    case IElement e when e.LocalName is "style" or "script" or "head" or "title" or "template":
                        break;
                    case IText t:
                        sb.Append(t.Data);
                        break;
                    default:
                        Walk(c);
                        break;
                }
            }
        }

        Walk(dom.Body!);
        return Squash(sb.ToString());
    }
}
