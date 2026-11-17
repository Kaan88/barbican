// A small stand-in for editor-core's DocHtml in the pipeline tests (Export / ToText / Import*): paragraphs as divs, the quoted original as a blockquote type=cite, an inline image as <img src="cid:...">.

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Barbican.Core.Compose;
using Barbican.Core.Editor;

namespace Barbican.Core.Tests.Compose;

internal sealed partial class TestCodec : IDocCodec
{
    public string Export(Doc doc)
    {
        var sb = new StringBuilder();
        foreach (var b in doc.Blocks)
        {
            switch (b.Kind)
            {
                case BlockKind.Quoted q:
                    sb.Append("<blockquote type=\"cite\">").Append(q.Html).Append("</blockquote>");
                    break;
                case BlockKind.Image { Data: { } d }:
                    sb.Append("<div><img src=\"").Append(d.Cid is { } c ? "cid:" + c : d.Src).Append("\"></div>");
                    break;
                default:
                    sb.Append("<div>").Append(WebUtility.HtmlEncode(b.Text)).Append("</div>");
                    break;
            }
        }

        return sb.ToString();
    }

    public string ToText(Doc doc) => string.Join('\n', doc.Blocks.Select(b => b.Kind is BlockKind.Quoted q ? string.Join('\n', q.Text.Split('\n').Select(l => "> " + l)) : b.Text));

    public IReadOnlyList<Block> Import(string html)
    {
        var blocks = new List<Block>();
        foreach (var part in Image().Split(html))
        {
            if (part.StartsWith("cid:", StringComparison.Ordinal))
            {
                blocks.Add(new Block(new BlockKind.Image(new ImageData { Src = part, Cid = part[4..] })));
            }
            else if (HtmlText(part).Length > 0)
            {
                blocks.AddRange(HtmlText(part).Split('\n').Select(Block.Para));
            }
        }

        return blocks;
    }

    public IReadOnlyList<Block> ImportText(string text) => [.. text.Split('\n').Select(Block.Para)];

    public IReadOnlyList<Block> ImportWithQuotes(string html)
    {
        var blocks = new List<Block>();
        var at = 0;
        foreach (Match m in Quote().Matches(html))
        {
            blocks.AddRange(Import(html[at..m.Index]));
            blocks.Add(new Block(new BlockKind.Quoted(m.Groups["inner"].Value, HtmlText(m.Groups["inner"].Value), false, true)));
            at = m.Index + m.Length;
        }

        blocks.AddRange(Import(html[at..]));
        return blocks;
    }

    public string CleanQuotedHtml(string html) => html;

    public string HtmlText(string html) => WebUtility.HtmlDecode(Tags().Replace(html.Replace("</div>", "\n", StringComparison.Ordinal).Replace("<br>", "\n", StringComparison.Ordinal), "")).Trim();

    // splits "<img src="cid:x">" out as a "cid:x" part
    [GeneratedRegex("<img src=\"(?<cid>cid:[^\"]+)\">", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ImageRegex();

    private static Regex Image() => ImageRegex();

    [GeneratedRegex("(?s)<blockquote type=\"cite\">(?<inner>.*?)</blockquote>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Quote();

    [GeneratedRegex("<[^>]*>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Tags();
}
