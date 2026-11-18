// The logical lines of a vCard file: RFC 6350 3.2 unfolding, vCard 2.1 quoted-printable soft breaks, and the line limits of G-012 (a line over the limit is cut, a photo over its limit is reported dropped). Streams the file: no copy of it is made.

using System.Text;

namespace Barbican.Core.Contacts;

public static partial class VCardParser
{
    private const string QuotedPrintable = ";ENCODING=QUOTED-PRINTABLE";

    private readonly record struct Logical(string Text, bool PhotoDropped);

    private static IEnumerable<Logical> LogicalLines(string text, VCardLimits limits)
    {
        var pos = 0;
        while (pos < text.Length)
        {
            var (first, next) = Physical(text, pos);
            pos = next;
            var photo = IsMediaLine(first);
            var cap = photo ? limits.MaxPhotoChars + 1024 : limits.MaxLineChars;
            var sb = new StringBuilder(first.Length <= cap ? first : first[..cap]);
            var over = first.Length > cap;

            // folded continuation lines: a line starting with a space or tab goes on the one before (the fold's one character is dropped)
            while (pos < text.Length)
            {
                var (line, after) = Physical(text, pos);
                if (line.Length == 0 || (line[0] != ' ' && line[0] != '\t'))
                {
                    break;
                }

                pos = after;
                Append(sb, line.AsSpan(1), cap, ref over);
            }

            // vCard 2.1 quoted-printable: a trailing "=" joins the next line
            if (HasQuotedPrintable(sb))
            {
                while (sb.Length > 0 && sb[^1] == '=' && pos < text.Length)
                {
                    sb.Length--;
                    var (joined, after) = Physical(text, pos);
                    pos = after;
                    Append(sb, joined.AsSpan(), cap, ref over);
                }
            }

            if (photo)
            {
                var all = sb.ToString();
                var colon = all.IndexOf(':', StringComparison.Ordinal);
                var valueChars = colon < 0 ? 0 : all.Length - colon - 1;
                yield return over || valueChars > limits.MaxPhotoChars ? new Logical(string.Empty, true) : new Logical(all, false);
            }
            else
            {
                yield return new Logical(sb.ToString(), false);
            }
        }
    }

    private static void Append(StringBuilder sb, ReadOnlySpan<char> part, int cap, ref bool over)
    {
        if (over)
        {
            return;
        }

        var room = cap - sb.Length;
        if (part.Length > room)
        {
            _ = sb.Append(part[..Math.Max(room, 0)]);
            over = true;
        }
        else
        {
            _ = sb.Append(part);
        }
    }

    private static bool HasQuotedPrintable(StringBuilder sb)
    {
        // only the start of a long line can name its encoding: no need to search a megabyte of base64
        var head = sb.Length <= 512 ? sb.ToString() : sb.ToString(0, 512);
        return head.Contains(QuotedPrintable, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One physical line (CRLF, LF or a lone CR ends it; NULs are dropped) and where the next starts.</summary>
    private static (string Line, int Next) Physical(string text, int pos)
    {
        var rest = text.AsSpan(pos);
        var i = rest.IndexOfAny('\r', '\n');
        var end = i < 0 ? text.Length : pos + i;
        var next = end;
        if (end < text.Length)
        {
            next = text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? end + 2 : end + 1;
        }

        var line = text.Substring(pos, end - pos);
        return (line.Contains('\0', StringComparison.Ordinal) ? line.Replace("\0", string.Empty, StringComparison.Ordinal) : line, next);
    }

    /// <summary>True for a PHOTO or LOGO property (with or without a group): those lines may be as long as a photo.</summary>
    private static bool IsMediaLine(string line)
    {
        var stop = line.AsSpan(0, Math.Min(line.Length, 64)).IndexOfAny(';', ':');
        var head = stop < 0 ? line.AsSpan(0, Math.Min(line.Length, 64)) : line.AsSpan(0, stop);
        var dot = head.LastIndexOf('.');
        if (dot >= 0)
        {
            head = head[(dot + 1)..];
        }

        return head.Equals("PHOTO", StringComparison.OrdinalIgnoreCase) || head.Equals("LOGO", StringComparison.OrdinalIgnoreCase);
    }
}
