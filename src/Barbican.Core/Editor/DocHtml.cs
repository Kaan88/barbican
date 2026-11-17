// Mirrors: Rust src/rte/html.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap HTML output), lib/email-html (inline styles); HTML Living Standard 13 (parsing, via AngleSharp).
// Decisions: spec/decisions.md D-M3B-02 (import over the AngleSharp DOM), D-M3B-05 (sanitising), D-M3B-06 (draft quote detection accepts any quoting of type=cite).

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Barbican.Core.Editor;

/// <summary>HTML in and out of the document model (port of Rust <c>rte/html.rs</c>).</summary>
public static partial class DocHtml
{
    private static readonly string[] SourceBlockEnds =
        ["</p>", "</h1>", "</h2>", "</h3>", "</li>", "</ul>", "</ol>", "</tr>", "</table>", "</blockquote>", "</pre>", "</div>"];

    private static readonly string[] SourceBlockOpens = ["<ul", "<ol", "<table", "<tr"];

    /// <summary>Tolerant HTML import (AngleSharp DOM): inline marks, blocks, nested lists, tables, images; whitespace collapsed, entities decoded, style/script/comments dropped. Never throws.</summary>
    public static IReadOnlyList<Block> Import(string html) => HtmlImporter.Import(html);

    /// <summary>Plain text to paragraph blocks (one per line).</summary>
    public static IReadOnlyList<Block> ImportText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return [.. text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(Block.Para)];
    }

    /// <summary>A whole message body (drafts): a top-level blockquote type=cite becomes the collapsible quoted block again.</summary>
    public static IReadOnlyList<Block> ImportWithQuotes(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var m = CiteOpen().Match(html);
        var gt = m.Success ? html.IndexOf('>', m.Index + m.Length) : -1;
        if (gt < 0)
        {
            return Import(html);
        }

        var innerStart = gt + 1;
        var end = MatchingClose(html, innerStart);
        if (end < 0)
        {
            return Import(html);
        }

        var closeGt = html.IndexOf('>', end);
        var after = closeGt < 0 ? html.Length : closeGt + 1;
        var blocks = Import(html[..m.Index]).ToList();
        while (blocks.Count > 1 && blocks[^1] is { Kind: BlockKind.Para } last && last.IsEmpty)
        {
            blocks.RemoveAt(blocks.Count - 1);
        }

        var inner = QuotedHtmlSanitizer.Sanitize(html[innerStart..end]);
        blocks.Add(new Block(new BlockKind.Quoted(inner, HtmlText(inner), Open: false, Cite: true)));
        var tail = Import(html[after..]);
        if (!(tail.Count == 1 && tail[0].IsEmpty))
        {
            blocks.AddRange(tail);
        }

        return blocks;
    }

    /// <summary>Bulwark's exact email-safe inline-style HTML in the wrapper div (hand-written, snapshot-tested).</summary>
    public static string Export(Doc doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return HtmlExporter.Export(doc);
    }

    /// <summary>The text/plain alternative.</summary>
    public static string ToText(Doc doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return HtmlExporter.ToText(doc);
    }

    /// <summary>HTML escaping as the exporter does it (two spaces become space plus nbsp).</summary>
    public static string Esc(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return HtmlExporter.Esc(text);
    }

    /// <summary>Named and numeric entities; an unknown or malformed one stays as written.</summary>
    public static string DecodeEntities(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains('&', StringComparison.Ordinal))
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        var rest = 0;
        while (true)
        {
            var p = text.IndexOf('&', rest);
            if (p < 0)
            {
                break;
            }

            sb.Append(text, rest, p - rest);
            var semi = text.IndexOf(';', p + 1);
            if (semi > p + 1 && semi - (p + 1) <= 10 && DecodeEntity(text.AsSpan(p + 1, semi - p - 1)) is { } decoded)
            {
                sb.Append(decoded);
                rest = semi + 1;
            }
            else
            {
                sb.Append('&');
                rest = p + 1;
            }
        }

        sb.Append(text, rest, text.Length - rest);
        return sb.ToString();
    }

    /// <summary>A CSS colour (rgb(), #rgb, #rrggbb) as #rrggbb, or null.</summary>
    public static string? NormColor(string value) => HtmlCss.NormColor(value);

    /// <summary>Drops document wrappers, head, styles and scripts so a quote cannot restyle the reply, and sanitises what stays (event handlers, frames, forms, scripting URLs; D-M3C-01).</summary>
    public static string CleanQuotedHtml(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var s = html;
        foreach (var tag in new[] { "script", "style", "head", "title" })
        {
            while (s.IndexOf("<" + tag, StringComparison.OrdinalIgnoreCase) is var a and >= 0)
            {
                var close = "</" + tag + ">";
                var e = s.IndexOf(close, a, StringComparison.OrdinalIgnoreCase);
                s = s.Remove(a, (e < 0 ? s.Length : e + close.Length) - a);
            }
        }

        // keep only the <body> content
        var body = s.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (body >= 0 && s.IndexOf('>', body) is var gt and >= 0)
        {
            var start = gt + 1;
            var end = s.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            s = s[start..(end < start ? s.Length : end)];
        }

        foreach (var t in new[] { "<!doctype", "<html", "</html", "<meta", "<?xml" })
        {
            while (s.IndexOf(t, StringComparison.OrdinalIgnoreCase) is var a and >= 0)
            {
                var e = s.IndexOf('>', a);
                s = s.Remove(a, (e < 0 ? s.Length : e + 1) - a);
            }
        }

        return QuotedHtmlSanitizer.Sanitize(s.Trim());
    }

    /// <summary>Plain text of an HTML fragment (the collapsed quote and text/plain).</summary>
    public static string HtmlText(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return ToText(new Doc(Import(html)));
    }

    /// <summary>Index of the first quoted block: the source view edits what comes before it.</summary>
    public static int EditableEnd(Doc doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var i = doc.Blocks.ToList().FindIndex(b => b.Kind is BlockKind.Quoted);
        return i < 0 ? doc.Blocks.Count : i;
    }

    /// <summary>The editable part as readable HTML, one block per line, without the wrapper div.</summary>
    public static string Source(Doc doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var d = new Doc(doc.Blocks.Take(EditableEnd(doc)));
        if (d.Blocks.Count == 0)
        {
            return "";
        }

        var h = Export(d);
        var inner = h.StartsWith(HtmlExporter.WrapOpen, StringComparison.Ordinal) && h.EndsWith("</div>", StringComparison.Ordinal)
            ? h[HtmlExporter.WrapOpen.Length..^"</div>".Length]
            : h;
        return Pretty(inner);
    }

    /// <summary>Line breaks after block-level tags (the importer ignores whitespace between blocks).</summary>
    public static string Pretty(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var o = new StringBuilder(html.Length + (html.Length / 16));
        var i = 0;
        while (i < html.Length)
        {
            var end = EndTagAt(html, i);
            if (end is not null)
            {
                o.Append(html, i, end.Length).Append('\n');
                i += end.Length;
                continue;
            }

            if (OpensBlockAt(html, i) && html.IndexOf('>', i) is var gt and >= 0)
            {
                o.Append(html, i, gt + 1 - i).Append('\n');
                i = gt + 1;
                continue;
            }

            o.Append(html[i]);
            i++;
        }

        return o.ToString().TrimEnd('\n');
    }

    /// <summary>The document after editing the source: the new editable part (keeping uploaded image data) plus the untouched quoted tail.</summary>
    public static Doc ApplySource(Doc doc, string source)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(source);
        var blocks = Import(source).ToList();
        if (blocks.Count == 0)
        {
            blocks.Add(new Block(new BlockKind.Para()));
        }

        // The source only carries src/alt/size: keep the attachment data (uploaded blob, bytes, type, name) of the images that were already in the document.
        var end = EditableEnd(doc);
        var old = doc.Blocks.Take(end).Select(b => b.Kind).OfType<BlockKind.Image>().Select(k => k.Data).ToList();
        foreach (var b in blocks)
        {
            if (b.Kind is not BlockKind.Image { Data: var img })
            {
                continue;
            }

            var same = old.Find(o => img.Cid is { } c ? string.Equals(o.Cid, c, StringComparison.Ordinal) : o.Cid is null && string.Equals(o.Src, img.Src, StringComparison.Ordinal));
            if (same is not null)
            {
                b.Kind = new BlockKind.Image(img with
                {
                    BlobId = img.BlobId ?? same.BlobId,
                    Mime = img.Mime.Length == 0 ? same.Mime : img.Mime,
                    Name = img.Name.Length == 0 ? same.Name : img.Name,
                    Bytes = img.Bytes ?? same.Bytes,
                });
            }
        }

        blocks.AddRange(doc.Blocks.Skip(end).Select(b => b.Clone()));
        var d = new Doc(blocks);
        d.Fix();
        return d;
    }

    [GeneratedRegex("<blockquote\\b[^>]*?\\btype\\s*=\\s*[\"']?cite[\"']?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex CiteOpen();

    /// <summary>Index of the close tag that matches a blockquote opened just before <paramref name="from"/>, or -1.</summary>
    private static int MatchingClose(string html, int from)
    {
        var depth = 1;
        var i = from;
        while (i < html.Length)
        {
            var o = html.IndexOf("<blockquote", i, StringComparison.OrdinalIgnoreCase);
            var c = html.IndexOf("</blockquote", i, StringComparison.OrdinalIgnoreCase);
            if (c < 0)
            {
                return -1;
            }

            if (o >= 0 && o < c)
            {
                depth++;
                i = o + "<blockquote".Length;
                continue;
            }

            if (--depth == 0)
            {
                return c;
            }

            i = c + "</blockquote".Length;
        }

        return -1;
    }

    private static string? EndTagAt(string html, int i)
    {
        foreach (var e in SourceBlockEnds)
        {
            if (string.Compare(html, i, e, 0, e.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return e;
            }
        }

        return null;
    }

    private static bool OpensBlockAt(string html, int i)
    {
        if (string.Compare(html, i, "<hr", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)
        {
            return true;
        }

        foreach (var t in SourceBlockOpens)
        {
            if (string.Compare(html, i, t, 0, t.Length, StringComparison.OrdinalIgnoreCase) == 0 && i + t.Length < html.Length && html[i + t.Length] is ' ' or '>')
            {
                return true;
            }
        }

        return false;
    }

    private static string? DecodeEntity(ReadOnlySpan<char> name)
    {
        if (name.Length > 1 && name[0] == '#')
        {
            var hex = name.Length > 2 && name[1] is 'x' or 'X';
            var digits = hex ? name[2..] : name[1..];
            if (int.TryParse(digits, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out var cp) && cp > 0 && Rune.IsValid(cp))
            {
                return char.ConvertFromUtf32(cp);
            }

            return null;
        }

        var entity = "&" + name.ToString() + ";";
        var decoded = WebUtility.HtmlDecode(entity);
        return string.Equals(decoded, entity, StringComparison.Ordinal) ? null : decoded;
    }
}
