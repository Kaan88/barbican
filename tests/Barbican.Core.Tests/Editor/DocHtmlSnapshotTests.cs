#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature
using Barbican.Core.Editor;

namespace Barbican.Core.Tests.Editor;

/// <summary>
/// The export of ten documents against files in <c>tests/fixtures/editor/export/</c>: the Rust exporter's output is the oracle (every block kind, nested lists, a table with a header,
/// a quote, an image with width, a code block, the wrapper, entity escaping, a link, coloured and sized spans). The matching <c>.txt</c> file is the text/plain rendering.
/// </summary>
public sealed class DocHtmlSnapshotTests
{
    private static Marks M(Func<Marks, Marks> f) => f(Marks.Plain);

    private static Block P(params Run[] runs)
    {
        var b = new Block(new BlockKind.Para());
        foreach (var r in runs)
        {
            b.Runs.Add(r);
        }

        b.Normalize();
        return b;
    }

    private static Run R(string text, Func<Marks, Marks>? f = null) => new(text, f is null ? Marks.Plain : f(Marks.Plain));

    private static Block Item(string text, BlockKind kind, int indent) => new(kind) { Indent = indent, Runs = { new Run(text, Marks.Plain) } };

    private static Block Cell(int table, int row, int col, bool header, string text) =>
        new(new BlockKind.Cell(table, row, col, header)) { Runs = { new Run(text, Marks.Plain) } };

    private static Doc Fixed(params Block[] blocks)
    {
        var d = new Doc(blocks);
        d.Fix();
        return d;
    }

    public static TheoryData<string> Names =>
    [
        "paragraphs-and-headings", "marks", "nested-lists", "table-with-header", "quote-and-code", "image-with-width", "rule-and-alignment",
        "entities-and-spaces", "links", "colour-and-size",
    ];

    private static Doc Build(string name) => name switch
    {
        "paragraphs-and-headings" => Fixed(
            new Block(new BlockKind.Heading(1)) { Runs = { new Run("Title", Marks.Plain) } },
            Block.Para("First paragraph."),
            new Block(new BlockKind.Heading(2)) { Runs = { new Run("Sub", Marks.Plain) }, Align = Align.Center },
            new Block(new BlockKind.Heading(3)) { Runs = { new Run("Small", Marks.Plain) } },
            Block.Para(""),
            Block.Para("after an empty line")),
        "marks" => Fixed(P(
            R("plain "), R("bold", m => m with { Bold = true }), R(" "), R("italic", m => m with { Italic = true }), R(" "), R("under", m => m with { Underline = true }),
            R(" "), R("strike", m => m with { Strike = true }), R(" "), R("code", m => m with { Code = true }), R(" "),
            R("all", m => m with { Bold = true, Italic = true, Underline = true, Strike = true }))),
        "nested-lists" => Fixed(
            Item("one", new BlockKind.Bullet(), 0), Item("two", new BlockKind.Bullet(), 0), Item("two a", new BlockKind.Ordered(), 1),
            Item("two b", new BlockKind.Ordered(), 1), Item("three", new BlockKind.Bullet(), 0), Item("deep", new BlockKind.Bullet(), 2),
            Block.Para("end"), Item("n1", new BlockKind.Ordered(), 0), Item("n2", new BlockKind.Ordered(), 0)),
        "table-with-header" => Fixed(
            Block.Para("before"),
            Cell(0, 0, 0, true, "Name"), Cell(0, 0, 1, true, "Qty"),
            Cell(0, 1, 0, false, "apple"), Cell(0, 1, 1, false, "3"),
            Cell(0, 2, 0, false, "pear"), new Block(new BlockKind.Cell(0, 2, 1, Header: false)),
            Block.Para("after")),
        "quote-and-code" => Fixed(
            Item("quoted one", new BlockKind.Quote(), 0), Item("quoted two", new BlockKind.Quote(), 0),
            Block.Para("between"),
            new Block(new BlockKind.CodeBlock()) { Runs = { new Run("if (a < b) {", Marks.Plain) } },
            new Block(new BlockKind.CodeBlock()) { Runs = { new Run("  return a & b;", Marks.Plain) } },
            new Block(new BlockKind.CodeBlock())),
        "image-with-width" => Fixed(
            new Block(new BlockKind.Image(new ImageData { Src = "https://x.example/a.png", Alt = "a \"quoted\" alt", Width = 320, Height = 160 })),
            new Block(new BlockKind.Image(new ImageData { Src = "cid:logo@x.example", Cid = "logo@x.example", Name = "logo.png" })) { Align = Align.Center },
            Block.Para("caption")),
        "rule-and-alignment" => Fixed(
            new Block(new BlockKind.Para()) { Align = Align.Right, Runs = { new Run("right", Marks.Plain) } },
            new Block(new BlockKind.Rule()),
            new Block(new BlockKind.Para()) { Align = Align.Justify, Runs = { new Run("justified text", Marks.Plain) } },
            new Block(new BlockKind.Bullet()) { Align = Align.Center, Runs = { new Run("centred item", Marks.Plain) } }),
        "entities-and-spaces" => Fixed(
            Block.Para("a<b>&\"c\"  two spaces   three"),
            Block.Para("tab\there"),
            Block.Para("line\nbreak"),
            Block.Para("trailing break\n"),
            Block.Para("\U0001F600 and é")),
        "links" => Fixed(
            P(R("see "), R("site", m => m with { Link = "https://x.example/?a=1&b=2" }), R(" or "), R("mail", m => m with { Link = "mailto:me@x.example", Bold = true })),
            P(R("coloured", m => m with { Link = "https://y.example", Color = "#ff0000" }))),
        "colour-and-size" => Fixed(P(
            R("red", m => m with { Color = "#ff0000" }), R(" "), R("marked", m => m with { Highlight = "#fef08a" }), R(" "), R("big", m => m with { Size = 24 }),
            R(" "), R("all", m => m with { Color = "#00ff00", Highlight = "#000000", Size = 10, Bold = true }))),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void Export_matches_the_snapshot(string name)
    {
        var d = Build(name);
        Snapshot.Match($"export/{name}.html", DocHtml.Export(d));
        Snapshot.Match($"export/{name}.txt", DocHtml.ToText(d));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Export_then_import_then_export_is_stable(string name)
    {
        var d = Build(name);
        var once = DocHtml.Export(d);
        var again = DocHtml.Export(new Doc(DocHtml.Import(once)));
        // a second pass changes nothing the first pass kept (empty trailing paragraphs and the like are already normalised)
        var twice = DocHtml.Export(new Doc(DocHtml.Import(again)));
        Assert.Equal(again, twice);
    }
}
