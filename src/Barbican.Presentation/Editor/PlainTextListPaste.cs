// Mirrors: Bulwark components/email/plain-text-paste.ts (one paragraph per line, blank lines included; "- ", "* ", bullet, "1. " and "1) " items become lists with nesting by indent).
// The document model has no list-item paragraphs: a continuation line of an item joins its block with a hard break, and an ordered list always counts from 1 (I-268).

using System.Text.RegularExpressions;
using Barbican.Core.Editor;

namespace Barbican.Presentation.Editor;

/// <summary>Turns pasted plain text into blocks the way typing it would, with lists recognised.</summary>
public static partial class PlainTextListPaste
{
    [GeneratedRegex(@"^(?<indent>[ 	]*)(?:[-*•]|(?<num>\d{1,3})[.)])[ 	]+(?=\S)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ItemMarkerPattern();

    private abstract record Part;

    private sealed record LinePart(string Text) : Part;

    private sealed record ListPart(bool Ordered, List<List<Part>> Items) : Part;

    private sealed record Marker(int Indent, bool Ordered, string Content);

    public static IReadOnlyList<Block> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var blocks = new List<Block>();
        Flatten(ParseLines(lines), 0, blocks);
        return blocks;
    }

    private static bool IsBlank(string line) => line.Trim().Length == 0;

    private static int IndentOf(string line)
    {
        var n = 0;
        foreach (var c in line)
        {
            if (c == ' ')
            {
                n++;
            }
            else if (c == '\t')
            {
                n += 4;
            }
            else
            {
                break;
            }
        }

        return n;
    }

    private static string Dedent(string line, int columns)
    {
        var i = 0;
        for (var col = 0; i < line.Length && col < columns; i++)
        {
            if (line[i] == ' ')
            {
                col++;
            }
            else if (line[i] == '\t')
            {
                col += 4;
            }
            else
            {
                break;
            }
        }

        return line[i..];
    }

    private static Marker? ItemMarker(string line)
    {
        var m = ItemMarkerPattern().Match(line);
        return m.Success ? new Marker(IndentOf(m.Groups["indent"].Value), m.Groups["num"].Success, line[m.Length..]) : null;
    }

    private static List<Part> ParseLines(IReadOnlyList<string> lines)
    {
        var blocks = new List<Part>();
        var i = 0;
        while (i < lines.Count)
        {
            if (ItemMarker(lines[i]) is { } marker)
            {
                var items = new List<List<Part>>();
                i = ParseList(lines, i, marker, items);
                blocks.Add(new ListPart(marker.Ordered, items));
            }
            else
            {
                blocks.Add(new LinePart(IsBlank(lines[i]) ? string.Empty : lines[i]));
                i++;
            }
        }

        return blocks;
    }

    private static int ParseList(IReadOnlyList<string> lines, int start, Marker first, List<List<Part>> items)
    {
        var i = start;
        var marker = first;
        while (true)
        {
            var body = new List<(string Text, bool Indented)>();
            var j = i + 1;
            var end = j;
            var lazy = true;
            while (j < lines.Count)
            {
                var line = lines[j];
                if (IsBlank(line))
                {
                    lazy = false;
                    j++;
                    continue;
                }

                var indented = IndentOf(line) > marker.Indent;
                if (!indented && !(lazy && ItemMarker(line) is null))
                {
                    break;
                }

                for (var k = end; k < j; k++)
                {
                    body.Add((string.Empty, false));
                }

                body.Add((line, indented));
                end = ++j;
                lazy = true;
            }

            var indents = body.Where(l => l.Indented).Select(l => IndentOf(l.Text)).ToList();
            var shift = indents.Count > 0 ? indents.Min() : 0;
            var rest = body.Select(l => l.Indented ? Dedent(l.Text, shift) : l.Text.TrimStart()).ToList();
            var item = new List<Part> { new LinePart(marker.Content) };
            item.AddRange(ParseLines(rest));
            items.Add(item);

            var next = end;
            while (next < lines.Count && IsBlank(lines[next]))
            {
                next++;
            }

            var sibling = next < lines.Count ? ItemMarker(lines[next]) : null;
            if (sibling is null || sibling.Ordered != first.Ordered || sibling.Indent != first.Indent)
            {
                return end;
            }

            i = next;
            marker = sibling;
        }
    }

    private static void Flatten(List<Part> parts, int indent, List<Block> output)
    {
        foreach (var part in parts)
        {
            if (part is LinePart line)
            {
                output.Add(Block.Para(line.Text));
                continue;
            }

            var list = (ListPart)part;
            foreach (var item in list.Items)
            {
                var kind = list.Ordered ? (BlockKind)new BlockKind.Ordered() : new BlockKind.Bullet();
                var head = Block.WithText(kind, ((LinePart)item[0]).Text, Marks.Plain);
                head.Indent = indent;
                output.Add(head);
                foreach (var rest in item.Skip(1))
                {
                    if (rest is LinePart cont)
                    {
                        if (cont.Text.Length > 0)
                        {
                            head.Runs.Add(new Run("\n" + cont.Text, Marks.Plain));
                        }
                    }
                    else
                    {
                        Flatten([rest], indent + 1, output);
                    }
                }

                head.Normalize();
            }
        }
    }
}
