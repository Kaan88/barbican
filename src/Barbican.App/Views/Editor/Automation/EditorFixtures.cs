#if AUTOMATION
// Mirrors: M3 plan 6.3 step 4 (`editor-open <fixture>`); sample data only (CLAUDE.md "Privacy": screenshots show fixtures, never a real mailbox).

using Barbican.Core.Editor;
using SkiaSharp;

namespace Barbican.App.Views.Editor.Automation;

/// <summary>Documents the automation commands open.</summary>
internal static class EditorFixtures
{
    public static Doc Create(string name)
    {
        var doc = name switch
        {
            "sample" => Sample(),
            "long" => new Doc(Enumerable.Range(0, 60).Select(i => Block.Para($"Paragraph {i + 1}: the quick brown fox jumps over the lazy dog, and then jumps again to make the line wrap."))),
            "spell" => new Doc([Block.Para("This is teh speling test, with a few words that are fine."), Block.Para("Another paragraph with xyzzy in it.")]),
            "rtl" => new Doc([Block.Para("Hello שלום עולם and مرحبا بالعالم mixed."), Block.Para("שלום עולם זהו משפט בעברית")]),
            "image" => WithImage(),
            "reply" => Reply(),
            "gate" => new Doc([Block.Para("The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly. Pack my box with five dozen liquor jugs, and sphinx of black quartz, judge my vow. How vexingly quick daft zebras jump over every wall.")]),
            "empty" => new Doc(),
            _ => new Doc([Block.Para(string.Empty)]),
        };
        doc.Fix();
        return doc;
    }

    private static Doc Sample()
    {
        var intro = Block.Para("Hello team, here is a short summary of what changed this week.");
        intro.MapMarks(6, 10, m => m with { Bold = true });
        intro.MapMarks(22, 35, m => m with { Italic = true });
        var link = Block.Para("See the plan at example.test for details and a highlighted word.");
        link.MapMarks(16, 28, m => m with { Link = "https://example.test/plan" });
        link.MapMarks(47, 58, m => m with { Highlight = "#fef08a" });
        var table = new List<Block>();
        for (var r = 0; r < 2; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                table.Add(Block.WithText(new BlockKind.Cell(1, r, c, r == 0), r == 0 ? $"Column {c + 1}" : $"cell {r}.{c + 1}", Marks.Plain));
            }
        }

        var blocks = new List<Block>
        {
            Block.WithText(new BlockKind.Heading(2), "Quarterly notes", Marks.Plain),
            intro,
            link,
            Block.WithText(new BlockKind.Bullet(), "First point", Marks.Plain),
            Block.WithText(new BlockKind.Bullet(), "Second point with a longer line that is not too short", Marks.Plain),
            Block.WithText(new BlockKind.Ordered(), "Step one", Marks.Plain),
            Block.WithText(new BlockKind.Ordered(), "Step two", Marks.Plain),
            Block.WithText(new BlockKind.Quote(), "A short quotation set apart from the text.", Marks.Plain),
            Block.WithText(new BlockKind.CodeBlock(), "var x = 1;", Marks.Plain),
        };
        blocks.AddRange(table);
        blocks.Add(new Block(new BlockKind.Rule()));
        blocks.Add(Block.Para("Thanks"));
        return new Doc(blocks);
    }

    private static Doc WithImage()
    {
        using var bmp = new SKBitmap(240, 120);
        using (var canvas = new SKCanvas(bmp))
        {
            using var paint = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(240, 120), [new SKColor(0xFFF0577A), new SKColor(0xFF4DABF7)], SKShaderTileMode.Clamp) };
            canvas.DrawRect(0, 0, 240, 120, paint);
        }

        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        var image = new ImageData { Bytes = data.ToArray(), Mime = "image/png", Name = "sample.png", Alt = "Sample picture", Cid = "imgsample@barbican" };
        return new Doc([Block.Para("A picture follows:"), new Block(new BlockKind.Image(image)), Block.Para("and a closing line.")]);
    }

    private static Doc Reply()
    {
        var quoted = new BlockKind.Quoted("<p>On Monday, someone wrote:</p><p>the original text</p>", "On Monday, someone wrote:\nthe original text of the earlier message that is being answered", false, true);
        return new Doc([Block.Para("Thanks, that works for me."), Block.Para(string.Empty), new Block(quoted)]);
    }
}
#endif
