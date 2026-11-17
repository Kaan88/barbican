// Mirrors: Rust src/rte/html.rs (tag rust-final) import() and its Builder; HTML Living Standard 13 (the tree comes from AngleSharp, so unclosed and
// misnested markup is repaired the way a browser does); Bulwark components/email/email-composer.tsx (TipTap schema). No Stalwart counterpart.
// Decisions: spec/decisions.md D-M3-B2 (AngleSharp DOM walk instead of the Rust tokenizer), D-M3-B5 (scripting schemes dropped from links and images).

using System.Globalization;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Barbican.Core.Editor;

/// <summary>
/// HTML into blocks. Walks the AngleSharp DOM iteratively (no recursion, so deeply nested input cannot overflow the stack) and feeds the same
/// open/text/close events to the same builder logic as the Rust importer. Only known tags and a few style properties reach the model, so
/// the result is sanitised by construction; <c>style</c>, <c>script</c>, <c>head</c>, <c>title</c> and comments are dropped.
/// </summary>
internal sealed class HtmlImporter
{
    private static readonly HashSet<string> RawElements = new(StringComparer.Ordinal) { "style", "script", "head", "title", "xml", "template" };
    private static readonly HashSet<string> Blocky = new(StringComparer.Ordinal)
    {
        "p", "div", "section", "article", "header", "footer", "center", "address", "main", "aside", "nav", "figure", "dd", "dt", "dl",
    };

    private readonly List<Block> _blocks = [];
    private readonly List<(string Name, Marks Marks)> _marks = [];
    private readonly List<bool> _lists = [];
    private readonly List<(string Name, Align Align)> _aligns = [];
    private Block? _cur;
    private int _quote;
    private int _pre;
    private bool _hasTable;
    private int _tableId;
    private int _tableRow;
    private int _tableCol;
    private int _tableDepth;
    private int _nextTable;
    private int? _heading;

    private HtmlImporter()
    {
    }

    private Marks CurMarks => _marks.Count > 0 ? _marks[^1].Marks : Marks.Plain;

    private Align CurAlign => _aligns.Count > 0 ? _aligns[^1].Align : Align.Left;

    private bool InCell => _hasTable && _tableDepth >= 1 && _cur is { Kind: BlockKind.Cell };

    /// <summary>Parses an HTML fragment into blocks (empty list for no content).</summary>
    public static List<Block> Import(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var dom = new HtmlParser().ParseDocument(html);
        var imp = new HtmlImporter();
        if (dom.Body is { } body)
        {
            imp.Walk(body);
        }

        return imp.Finish();
    }

    private static string? Attr(IElement e, string name) => e.GetAttribute(name);

    private static string? Style(IElement e, string prop) => HtmlCss.StyleProp(Attr(e, "style"), prop);

    private static bool IsScripting(string url)
    {
        // browsers ignore tabs, newlines and leading control characters inside a scheme
        var sb = new StringBuilder(16);
        foreach (var c in url)
        {
            if (c <= ' ')
            {
                continue;
            }

            sb.Append(char.ToLowerInvariant(c));
            if (sb.Length >= 11)
            {
                break;
            }
        }

        var s = sb.ToString();
        return s.StartsWith("javascript:", StringComparison.Ordinal) || s.StartsWith("vbscript:", StringComparison.Ordinal);
    }

    private static Align? AlignOf(IElement e)
    {
        var a = Style(e, "text-align") ?? Attr(e, "align");
        return a is null
            ? null
            : a.ToLowerInvariant() switch
            {
                "center" => Align.Center,
                "right" or "end" => Align.Right,
                "justify" => Align.Justify,
                _ => Align.Left,
            };
    }

    private static Marks InlineStyle(Marks m, IElement e)
    {
        if (Style(e, "font-weight") is { } w)
        {
            m = m with { Bold = w is "bold" or "bolder" || (uint.TryParse(w, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 600) };
        }

        if (Style(e, "font-style") is { } s)
        {
            m = m with { Italic = s is "italic" or "oblique" };
        }

        if ((Style(e, "text-decoration") ?? Style(e, "text-decoration-line")) is { } d)
        {
            if (d.Contains("underline", StringComparison.Ordinal))
            {
                m = m with { Underline = true };
            }

            if (d.Contains("line-through", StringComparison.Ordinal))
            {
                m = m with { Strike = true };
            }
        }

        if (Style(e, "color") is { } c && HtmlCss.NormColor(c) is { } col)
        {
            m = m with { Color = col };
        }

        if ((Style(e, "background-color") ?? Style(e, "background")) is { } bg && HtmlCss.NormColor(bg) is { } hl && !string.Equals(hl, "#ffffff", StringComparison.Ordinal))
        {
            m = m with { Highlight = hl };
        }

        if (Style(e, "font-size") is { } fs && HtmlCss.SizePx(fs) is { } px)
        {
            m = m with { Size = px != 14 ? px : null };
        }

        return m;
    }

    private static int? PxAttr(string? v)
    {
        if (v is null)
        {
            return null;
        }

        v = v.Trim();
        if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            v = v[..^2];
        }

        return int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static ImageData? ImageFrom(IElement e)
    {
        var src = Attr(e, "src")?.Trim();
        if (string.IsNullOrEmpty(src) || IsScripting(src))
        {
            return null;
        }

        var width = PxAttr(Attr(e, "width"));
        if (width is null && Style(e, "width") is { } sw && sw.Trim().EndsWith("px", StringComparison.OrdinalIgnoreCase)
            && float.TryParse(sw.Trim()[..^2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && float.IsFinite(f) && f >= 0 && f < int.MaxValue)
        {
            width = (int)f;
        }

        var img = new ImageData
        {
            Src = src,
            Alt = Attr(e, "alt") ?? "",
            Width = width ?? 0,
            Height = PxAttr(Attr(e, "height")) ?? 0,
        };
        if (src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
        {
            img = img with { Cid = src[4..] };
        }

        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && src.IndexOf(',', StringComparison.Ordinal) is var comma and > 0)
        {
            var meta = src[5..comma];
            if (meta.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    img = img with { Bytes = Convert.FromBase64String(src[(comma + 1)..].Trim()), Mime = meta[..^";base64".Length] };
                }
                catch (FormatException)
                {
                    // a broken data URL keeps no bytes: the image shows as missing
                }
            }
        }

        return img;
    }

    /// <summary>Makes every table rectangular.</summary>
    private static void PadTables(List<Block> blocks)
    {
        var i = 0;
        while (i < blocks.Count)
        {
            if (blocks[i].Kind.Table is not int t)
            {
                i++;
                continue;
            }

            var s = i;
            while (i < blocks.Count && blocks[i].Kind.Table == t)
            {
                i++;
            }

            var d = new Doc(blocks.GetRange(s, i - s));
            var span = new BlockSpan(0, d.Blocks.Count);
            var (rows, cols) = d.TableDims(span);
            if (rows * cols == i - s)
            {
                continue;
            }

            var cells = new List<Block>();
            var grid = d.TableGrid(span);
            for (var ri = 0; ri < grid.Count; ri++)
            {
                for (var ci = 0; ci < grid[ri].Count; ci++)
                {
                    var c = grid[ri][ci];
                    c.Kind = new BlockKind.Cell(t, ri, ci, c.Kind is BlockKind.Cell { Header: true });
                    cells.Add(c);
                }
            }

            blocks.RemoveRange(s, i - s);
            blocks.InsertRange(s, cells);
            i = s + cells.Count;
        }
    }

    private BlockKind CurKind()
    {
        if (_hasTable && _tableDepth == 1)
        {
            return new BlockKind.Cell(_tableId, Math.Max(_tableRow, 0), Math.Max(_tableCol - 1, 0), Header: false);
        }

        if (_pre > 0)
        {
            return new BlockKind.CodeBlock();
        }

        if (_heading is int h)
        {
            return new BlockKind.Heading(h);
        }

        return _quote > 0 ? new BlockKind.Quote() : new BlockKind.Para();
    }

    private Block Ensure() => _cur ??= new Block(CurKind()) { Align = CurAlign };

    private void AppendRaw(string text)
    {
        var m = CurMarks;
        var b = Ensure();
        b.Insert(b.Length, text, m);
    }

    private void Flush()
    {
        if (_cur is not { } b)
        {
            return;
        }

        _cur = null;

        // a trailing <br> placeholder and trailing whitespace
        if (b.Runs.Count > 0)
        {
            var r = b.Runs[^1];
            var t = r.Text;
            if (b.Kind is not BlockKind.CodeBlock)
            {
                t = t.TrimEnd(' ', '\t');
            }

            if (t.EndsWith('\n'))
            {
                t = t[..^1];
            }

            b.Runs[^1] = r with { Text = t };
        }

        b.Normalize();
        _blocks.Add(b);
    }

    /// <summary>Starts a block-level element: inside table cells this is just a line break.</summary>
    private void BlockBreak()
    {
        if (InCell)
        {
            var b = _cur!;
            if (!b.IsEmpty && !b.Text.EndsWith('\n'))
            {
                b.Insert(b.Length, "\n", b.MarksAt(b.Length));
            }

            return;
        }

        if (_cur is { } c)
        {
            if (c.IsEmpty)
            {
                if (!c.Kind.IsList)
                {
                    _cur = null;
                }

                return;
            }

            // a <p> inside a list item continues the item on a new line
            if (c.Kind.IsList && _lists.Count > 0)
            {
                if (!c.Text.EndsWith('\n'))
                {
                    c.Insert(c.Length, "\n", c.MarksAt(c.Length));
                }

                return;
            }
        }

        Flush();
    }

    private void Text(string t)
    {
        if (_pre > 0)
        {
            AppendRaw(t);
            return;
        }

        // collapse whitespace (a space stays with the text it followed)
        var prevSpace = _cur is null || _cur.IsEmpty || _cur.Text.EndsWith(' ') || _cur.Text.EndsWith('\n');
        var sb = new StringBuilder(t.Length);
        foreach (var c in t)
        {
            if (char.IsWhiteSpace(c) && c != ' ')
            {
                if (!prevSpace)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }

                continue;
            }

            sb.Append(c == ' ' ? ' ' : c);
            prevSpace = false;
        }

        if (sb.Length > 0)
        {
            AppendRaw(sb.ToString());
        }
    }

    private void PushMarks(string name, Func<Marks, Marks> f) => _marks.Add((name, f(CurMarks)));

    private void PopTo(string name)
    {
        var i = _marks.FindLastIndex(m => string.Equals(m.Name, name, StringComparison.Ordinal));
        if (i >= 0)
        {
            _marks.RemoveRange(i, _marks.Count - i);
        }
    }

    private void PopAlign(string name)
    {
        var i = _aligns.FindLastIndex(a => string.Equals(a.Name, name, StringComparison.Ordinal));
        if (i >= 0)
        {
            _aligns.RemoveRange(i, _aligns.Count - i);
        }
    }

    private void Walk(IElement root)
    {
        var stack = new Stack<(INode Node, bool Closing)>();
        for (var c = root.LastChild; c is not null; c = c.PreviousSibling)
        {
            stack.Push((c, false));
        }

        while (stack.Count > 0)
        {
            var (node, closing) = stack.Pop();
            switch (node)
            {
                case IElement e when closing:
                    Close(e.LocalName.ToLowerInvariant());
                    break;
                case IElement e:
                    var name = e.LocalName.ToLowerInvariant();
                    if (RawElements.Contains(name))
                    {
                        break;
                    }

                    Open(name, e);
                    stack.Push((e, true));
                    for (var c = e.LastChild; c is not null; c = c.PreviousSibling)
                    {
                        stack.Push((c, false));
                    }

                    break;
                case IText t:
                    Text(t.Data);
                    break;
                default:
                    break; // comments, processing instructions, doctypes
            }
        }
    }

    // MA0051: one case per tag, as in the Rust importer; splitting the table scatters the order the builder state depends on.
#pragma warning disable MA0051
    private void Open(string n, IElement e)
    {
        var align = AlignOf(e);
        switch (n)
        {
            case "br":
                AppendRaw("\n");
                break;
            case var _ when Blocky.Contains(n):
                BlockBreak();
                var a = string.Equals(n, "center", StringComparison.Ordinal) ? Align.Center : align;
                _aligns.Add((n, a ?? CurAlign));
                if (_cur is { } cb && a is { } av)
                {
                    cb.Align = av;
                }

                // block-level inline styles (e.g. <div style="font-weight:bold">)
                if (Attr(e, "style") is not null)
                {
                    PushMarks(n, m => InlineStyle(m, e));
                }

                break;
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                BlockBreak();
                Flush();
                _heading = Math.Min(n[1] - '0', 3);
                _aligns.Add((n, align ?? CurAlign));
                break;
            case "blockquote":
                BlockBreak();
                Flush();
                _quote++;
                break;
            case "pre":
                BlockBreak();
                Flush();
                _pre++;
                break;
            case "ul" or "ol":
                BlockBreak();
                if (!InCell)
                {
                    Flush();
                }

                _lists.Add(string.Equals(n, "ol", StringComparison.Ordinal));
                break;
            case "li":
                if (InCell)
                {
                    BlockBreak();
                    break;
                }

                Flush();
                _cur = new Block(_lists.Count > 0 && _lists[^1] ? new BlockKind.Ordered() : new BlockKind.Bullet())
                {
                    Indent = Math.Max(_lists.Count - 1, 0),
                    Align = CurAlign,
                };
                break;
            case "hr":
                BlockBreak();
                Flush();
                _blocks.Add(new Block(new BlockKind.Rule()));
                break;
            case "table":
                _tableDepth++;
                BlockBreak();
                if (_tableDepth == 1)
                {
                    Flush();
                    (_hasTable, _tableId, _tableRow, _tableCol) = (true, _nextTable, -1, 0);
                    _nextTable++;
                }

                break;
            case "tr":
                if (_tableDepth == 1)
                {
                    _tableRow++;
                    _tableCol = 0;
                    Flush();
                }
                else
                {
                    BlockBreak();
                }

                break;
            case "td" or "th":
                OpenCell(n, align);
                break;
            case "img":
                OpenImage(e);
                break;
            case "b" or "strong":
                PushMarks(n, m => InlineStyle(m with { Bold = true }, e));
                break;
            case "i" or "em" or "cite" or "var":
                PushMarks(n, m => InlineStyle(m with { Italic = true }, e));
                break;
            case "u" or "ins":
                PushMarks(n, m => InlineStyle(m with { Underline = true }, e));
                break;
            case "s" or "strike" or "del":
                PushMarks(n, m => InlineStyle(m with { Strike = true }, e));
                break;
            case "code" or "tt" or "kbd" or "samp":
                var inPre = _pre > 0;
                PushMarks(n, m => m with { Code = !inPre });
                break;
            case "mark":
                PushMarks(n, m => InlineStyle(m with { Highlight = "#fef08a" }, e));
                break;
            case "a":
                var href = Attr(e, "href");
                var keep = !string.IsNullOrEmpty(href) && !href.StartsWith('#') && !IsScripting(href);
                PushMarks(n, m => InlineStyle(m with { Link = keep ? href : null }, e));
                break;
            case "font":
                PushMarks(n, m =>
                {
                    if (Attr(e, "color") is { } fc && HtmlCss.NormColor(fc) is { } col)
                    {
                        m = m with { Color = col };
                    }

                    if (Attr(e, "size") is { } fz && HtmlCss.FontSizeAttr(fz) is { } px)
                    {
                        m = m with { Size = px != 14 ? px : null };
                    }

                    return InlineStyle(m, e);
                });
                break;
            case "span" or "sub" or "sup" or "small" or "big" or "abbr" or "label" or "o:p":
                PushMarks(n, m => InlineStyle(string.Equals(n, "small", StringComparison.Ordinal) ? m with { Size = 12 } : m, e));
                break;
            default:
                break;
        }
    }

    private void Close(string n)
    {
        switch (n)
        {
            case var _ when Blocky.Contains(n):
                if (InCell)
                {
                    BlockBreak();
                }
                else if (_cur is { Kind.IsList: true })
                {
                    // </p> inside <li>: keep the item open
                }
                else if (_cur is null && string.Equals(n, "p", StringComparison.Ordinal))
                {
                    // an empty <p></p> still makes a line
                    _blocks.Add(new Block(CurKind()) { Align = CurAlign });
                }
                else
                {
                    Flush();
                }

                PopAlign(n);
                PopTo(n);
                break;
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                Flush();
                _heading = null;
                PopAlign(n);
                break;
            case "blockquote":
                Flush();
                _quote = Math.Max(_quote - 1, 0);
                break;
            case "pre":
                Flush();
                _pre = Math.Max(_pre - 1, 0);
                break;
            case "ul" or "ol":
                if (!InCell)
                {
                    Flush();
                }

                if (_lists.Count > 0)
                {
                    _lists.RemoveAt(_lists.Count - 1);
                }

                break;
            case "li":
                if (!InCell)
                {
                    Flush();
                }

                break;
            case "td" or "th":
                if (_tableDepth == 1)
                {
                    Flush();
                    PopTo("th");
                }

                break;
            case "tr":
                if (_tableDepth == 1)
                {
                    Flush();
                }

                break;
            case "table":
                if (_tableDepth == 1)
                {
                    Flush();
                    _hasTable = false;
                }

                _tableDepth = Math.Max(_tableDepth - 1, 0);
                break;
            default:
                PopTo(n);
                break;
        }
    }
#pragma warning restore MA0051

    private void OpenCell(string n, Align? align)
    {
        if (_tableDepth == 1)
        {
            Flush();
            if (_hasTable)
            {
                _tableRow = Math.Max(_tableRow, 0);
                _tableCol++;
                _cur = new Block(new BlockKind.Cell(_tableId, _tableRow, _tableCol - 1, Header: string.Equals(n, "th", StringComparison.Ordinal))) { Align = align ?? Align.Left };
            }

            if (string.Equals(n, "th", StringComparison.Ordinal))
            {
                PushMarks("th", m => m with { Bold = true });
            }

            return;
        }

        if (_cur is { IsEmpty: false } c)
        {
            c.Insert(c.Length, " ", CurMarks);
        }
    }

    private void OpenImage(IElement e)
    {
        if (ImageFrom(e) is not { } img)
        {
            return;
        }

        if (InCell)
        {
            Text("[" + (img.Alt.Length == 0 ? "image" : img.Alt) + "]");
            return;
        }

        BlockBreak();
        Flush();
        _blocks.Add(new Block(new BlockKind.Image(img)) { Align = CurAlign });
    }

    private List<Block> Finish()
    {
        Flush();
        var blocks = _blocks;

        // drop trailing empty paragraphs produced by wrappers
        while (blocks.Count > 1 && blocks[^1] is { Kind: BlockKind.Para } last && last.IsEmpty)
        {
            blocks.RemoveAt(blocks.Count - 1);
        }

        PadTables(blocks);
        return blocks;
    }
}
