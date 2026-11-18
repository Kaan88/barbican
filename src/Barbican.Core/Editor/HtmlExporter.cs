// Mirrors: Rust src/rte/html.rs (tag rust-final) export(), runs_html(), esc(), to_text(); Bulwark components/email/email-composer.tsx and lib/email-html (inline-style spacing:
// paragraphs 4 px, headings, lists, framed code, quote bar, link colour #2563eb). The Rust output is the oracle, kept byte for byte (tests/fixtures/editor/export/*.html).
// No RFC or Stalwart counterpart.

using System.Globalization;
using System.Text;

// MA0051: Export is one switch over the block kinds and RunsHtml one pass over the mark wrappers, in the order the Rust oracle nests them.
#pragma warning disable MA0051

namespace Barbican.Core.Editor;

/// <summary>The email-safe HTML export (all styles inline) and the text/plain rendering of a <see cref="Doc"/>.</summary>
internal static class HtmlExporter
{
    /// <summary>The wrapper div's opening tag (Bulwark's body font stack, 14 px, line height 1.5).</summary>
    public const string WrapOpen = "<div style=\"font-family:'Segoe UI',-apple-system,Roboto,Helvetica,Arial,sans-serif;font-size:14px;line-height:1.5\">";

    private const string LinkColor = "#2563eb";
    private const string CodeStyle = "font-family:Consolas,'Courier New',monospace;background-color:#f1f5f9;border-radius:3px;padding:1px 4px;font-size:90%";
    private const string ListStyle = "margin:4px 0;padding-left:24px";
    private const string TableOpen = "<table style=\"border-collapse:collapse;margin:6px 0\" cellpadding=\"0\" cellspacing=\"0\">";

    public static string Esc(string s)
    {
        var prevSpace = true;
        return EscRun(s, ref prevSpace);
    }

    public static string ImageSrc(ImageData img) => img.Cid is { } c ? "cid:" + c : img.Src;

    public static string Export(Doc doc)
    {
        var bl = doc.Blocks;
        var h = new StringBuilder(WrapOpen);
        var i = 0;
        while (i < bl.Count)
        {
            var b = bl[i];
            var al = AlignStyle(b.Align);
            switch (b.Kind)
            {
                case BlockKind.Para:
                    var inner = RunsHtml(b.Runs, inCode: false);
                    if (inner.Length == 0 || b.Text.EndsWith('\n'))
                    {
                        inner += "<br>";
                    }

                    h.Append("<p style=\"margin:4px 0;").Append(al).Append("\">").Append(inner).Append("</p>");
                    i++;
                    break;
                case BlockKind.Heading hd:
                    var (size, weight) = hd.Level switch { 1 => (24, 700), 2 => (20, 600), _ => (17, 600) };
                    var l = hd.Level.ToString(CultureInfo.InvariantCulture);
                    h.Append("<h").Append(l).Append(" style=\"font-size:").Append(size).Append("px;font-weight:").Append(weight)
                        .Append(";line-height:1.3;margin:8px 0;").Append(al).Append("\">").Append(RunsHtml(b.Runs, inCode: false)).Append("</h").Append(l).Append('>');
                    i++;
                    break;
                case BlockKind.Quote:
                    i = ExportQuote(doc, i, h);
                    break;
                case BlockKind.CodeBlock:
                    i = ExportCode(doc, i, h);
                    break;
                case BlockKind.Bullet or BlockKind.Ordered:
                    i = ExportList(doc, i, h);
                    break;
                case BlockKind.Cell:
                    i = ExportTable(doc, i, h);
                    break;
                case BlockKind.Image img:
                    ExportImage(img.Data, al, h);
                    i++;
                    break;
                case BlockKind.Quoted q:
                    if (q.Cite)
                    {
                        h.Append("<blockquote type=\"cite\" style=\"margin:0 0 0 .8ex;border-left:2px solid #d4d4d8;padding-left:1ex\">").Append(QuotedHtmlSanitizer.Sanitize(q.Html)).Append("</blockquote>");
                    }
                    else
                    {
                        h.Append("<div>").Append(QuotedHtmlSanitizer.Sanitize(q.Html)).Append("</div>");
                    }

                    i++;
                    break;
                case BlockKind.Rule:
                    h.Append("<hr style=\"border:none;border-top:1px solid #d4d4d8;margin:16px 0\">");
                    i++;
                    break;
                default:
                    i++;
                    break;
            }
        }

        return h.Append("</div>").ToString();
    }

    public static string ToText(Doc doc)
    {
        var bl = doc.Blocks;
        var output = new List<string>();
        var i = 0;
        while (i < bl.Count)
        {
            var b = bl[i];
            switch (b.Kind)
            {
                case BlockKind.Para or BlockKind.CodeBlock or BlockKind.Heading:
                    output.Add(RunsText(b.Runs));
                    break;
                case BlockKind.Quote:
                    output.AddRange(RunsText(b.Runs).Split('\n').Select(l => "> " + l));
                    break;
                case BlockKind.Bullet or BlockKind.Ordered:
                    var pad = string.Concat(Enumerable.Repeat("   ", b.Indent));
                    var marker = b.Kind is BlockKind.Ordered ? doc.Ordinal(i).ToString(CultureInfo.InvariantCulture) + ". " : "- ";
                    output.Add(pad + marker + RunsText(b.Runs).Replace("\n", "\n" + pad + "  ", StringComparison.Ordinal));
                    break;
                case BlockKind.Cell:
                    var r = doc.TableRange(i) ?? new BlockSpan(i, i + 1);
                    foreach (var row in doc.TableGrid(r))
                    {
                        output.Add(string.Join(" | ", row.Select(c => RunsText(c.Runs).Replace('\n', ' '))));
                    }

                    i = r.End;
                    continue;
                case BlockKind.Image img:
                    var d = img.Data;
                    output.Add("[" + (d.Alt.Length > 0 ? d.Alt : d.Name.Length > 0 ? d.Name : "image") + "]");
                    break;
                case BlockKind.Quoted q:
                    if (q.Cite)
                    {
                        output.AddRange(Doc.Lines(q.Text).Select(l => l.Length == 0 ? ">" : "> " + l));
                    }
                    else
                    {
                        output.Add(q.Text);
                    }

                    break;
                case BlockKind.Rule:
                    output.Add("----------");
                    break;
                default:
                    break;
            }

            i++;
        }

        return string.Join('\n', output).TrimEnd('\n');
    }

    public static string RunsHtml(IEnumerable<Run> runs, bool inCode)
    {
        var h = new StringBuilder();
        var prevSpace = true;
        foreach (var r in runs)
        {
            var m = r.Marks;
            var t = inCode
                ? r.Text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal)
                : EscRun(r.Text, ref prevSpace);
            if (m.Code && !inCode)
            {
                t = $"<code style=\"{CodeStyle}\">{t}</code>";
            }

            if (m.Strike)
            {
                t = $"<s>{t}</s>";
            }

            if (m.Underline)
            {
                t = $"<u>{t}</u>";
            }

            if (m.Italic)
            {
                t = $"<em>{t}</em>";
            }

            if (m.Bold)
            {
                t = $"<strong>{t}</strong>";
            }

            var style = new StringBuilder();
            if (m.Color is { } c)
            {
                style.Append("color:").Append(c).Append(';');
            }

            if (m.Highlight is { } hl)
            {
                style.Append("background-color:").Append(hl).Append(';');
            }

            if (m.Size is { } s)
            {
                style.Append("font-size:").Append(s).Append("px;");
            }

            if (style.Length > 0)
            {
                t = $"<span style=\"{style}\">{t}</span>";
            }

            if (m.Link is { } link)
            {
                t = $"<a href=\"{EscAttr(link)}\" style=\"color:{m.Color ?? LinkColor};text-decoration:underline\">{t}</a>";
            }

            h.Append(t);
        }

        return h.ToString();
    }

    /// <summary>Escapes text for HTML, keeping runs of spaces visible (<paramref name="prevSpace"/> carries across runs).</summary>
    private static string EscRun(string s, ref bool prevSpace)
    {
        var o = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '&':
                    o.Append("&amp;");
                    break;
                case '<':
                    o.Append("&lt;");
                    break;
                case '>':
                    o.Append("&gt;");
                    break;
                case '"':
                    o.Append("&quot;");
                    break;
                case '\n':
                    o.Append("<br>");
                    prevSpace = true;
                    continue;
                case ' ' when prevSpace:
                    o.Append("&nbsp;");
                    prevSpace = false;
                    continue;
                case '\t':
                    o.Append("&nbsp;&nbsp;&nbsp;&nbsp;");
                    prevSpace = false;
                    continue;
                default:
                    o.Append(c);
                    break;
            }

            prevSpace = c == ' ';
        }

        return o.ToString();
    }

    private static string EscAttr(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);

    private static string AlignStyle(Align a) => a == Align.Left ? "" : "text-align:" + a.Css() + ";";

    private static string CloseList(BlockKind k) => k is BlockKind.Ordered ? "</li></ol>" : "</li></ul>";

    private static string RunsText(IEnumerable<Run> runs)
    {
        var sb = new StringBuilder();
        foreach (var r in runs)
        {
            if (r.Marks.Link is { } l && !string.Equals(TrimMailto(l), r.Text.Trim(), StringComparison.Ordinal) && !r.Text.Contains(l, StringComparison.Ordinal))
            {
                sb.Append(r.Text).Append(" <").Append(l).Append('>');
            }
            else
            {
                sb.Append(r.Text);
            }
        }

        return sb.ToString();
    }

    private static string TrimMailto(string s)
    {
        while (s.StartsWith("mailto:", StringComparison.Ordinal))
        {
            s = s["mailto:".Length..];
        }

        return s;
    }

    private static int ExportQuote(Doc doc, int i, StringBuilder h)
    {
        var bl = doc.Blocks;
        h.Append("<blockquote style=\"margin:8px 0;padding:0 0 0 16px;border-left:3px solid #d4d4d8;color:#52525b\">");
        while (i < bl.Count && bl[i].Kind is BlockKind.Quote)
        {
            var inner = RunsHtml(bl[i].Runs, inCode: false);
            h.Append("<p style=\"margin:4px 0;").Append(AlignStyle(bl[i].Align)).Append("\">").Append(inner.Length == 0 ? "<br>" : inner).Append("</p>");
            i++;
        }

        h.Append("</blockquote>");
        return i;
    }

    private static int ExportCode(Doc doc, int i, StringBuilder h)
    {
        var bl = doc.Blocks;
        h.Append("<pre style=\"font-family:Consolas,'Courier New',monospace;font-size:13px;background-color:#f4f4f5;color:#18181b;border:1px solid #e4e4e7;border-radius:6px;padding:12px;margin:8px 0;white-space:pre-wrap\">");
        var first = true;
        while (i < bl.Count && bl[i].Kind is BlockKind.CodeBlock)
        {
            if (!first)
            {
                h.Append('\n');
            }

            first = false;
            h.Append(RunsHtml(bl[i].Runs, inCode: true));
            i++;
        }

        h.Append("</pre>");
        return i;
    }

    /// <summary>Nested lists from indent levels, one li per block.</summary>
    private static int ExportList(Doc doc, int i, StringBuilder h)
    {
        var bl = doc.Blocks;
        var stack = new List<BlockKind>();
        while (i < bl.Count && bl[i].Kind.IsList)
        {
            var it = bl[i];
            var lvl = it.Indent;
            while (stack.Count > lvl + 1)
            {
                h.Append(CloseList(Pop(stack)));
            }

            if (stack.Count == lvl + 1)
            {
                h.Append(stack[lvl] != it.Kind ? CloseList(Pop(stack)) : "</li>");
            }

            while (stack.Count < lvl + 1)
            {
                BlockKind k = stack.Count == lvl ? it.Kind : new BlockKind.Bullet();
                h.Append(k is BlockKind.Ordered ? "<ol style=\"" : "<ul style=\"").Append(ListStyle).Append("\">");
                if (stack.Count < lvl)
                {
                    h.Append("<li style=\"list-style:none\">");
                }

                stack.Add(k);
            }

            h.Append("<li style=\"margin:2px 0;").Append(AlignStyle(it.Align)).Append("\">").Append(RunsHtml(it.Runs, inCode: false));
            i++;
        }

        while (stack.Count > 0)
        {
            h.Append(CloseList(Pop(stack)));
        }

        return i;
    }

    private static int ExportTable(Doc doc, int i, StringBuilder h)
    {
        var r = doc.TableRange(i) ?? new BlockSpan(i, i + 1);
        h.Append(TableOpen);
        foreach (var row in doc.TableGrid(r))
        {
            h.Append("<tr>");
            foreach (var c in row)
            {
                var header = c.Kind is BlockKind.Cell { Header: true };
                var tag = header ? "th" : "td";
                var bg = header ? "background-color:#f4f4f5;font-weight:700;" : "";
                var inner = RunsHtml(c.Runs, inCode: false);
                h.Append('<').Append(tag).Append(" style=\"border:1px solid #d4d4d8;padding:6px 10px;vertical-align:top;text-align:left;").Append(bg).Append(AlignStyle(c.Align)).Append("\">")
                    .Append(inner.Length == 0 ? "<br>" : inner).Append("</").Append(tag).Append('>');
            }

            h.Append("</tr>");
        }

        h.Append("</table>");
        return r.End;
    }

    private static void ExportImage(ImageData img, string al, StringBuilder h)
    {
        var attrs = new StringBuilder($"src=\"{EscAttr(ImageSrc(img))}\" alt=\"{EscAttr(img.Alt)}\"");
        var style = "";
        if (img.Width > 0)
        {
            attrs.Append(" width=\"").Append(img.Width).Append('"');
            style = $"width:{img.Width}px;";
        }

        h.Append("<div style=\"").Append(al).Append("\"><img ").Append(attrs).Append(" style=\"").Append(style).Append("max-width:100%;height:auto\"></div>");
    }

    private static BlockKind Pop(List<BlockKind> s)
    {
        var k = s[^1];
        s.RemoveAt(s.Count - 1);
        return k;
    }
}
