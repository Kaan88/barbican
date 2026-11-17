// Mirrors: RFC 4155 (mbox: an envelope line "From <sender> <asctime>" before each message, a blank line after it) with mboxrd quoting (a body
// line matching ^>*From  gains one ">", which MboxSplitter takes off again); Bulwark lib/mail-export/mbox.ts (Status and X-Keywords lines
// written in front of the message so a re-import keeps read, flagged and the keywords). spec G-046. Owner unified.

using System.Globalization;
using System.Text;

namespace Barbican.Core.Mail.Import;

/// <summary>Writes messages to an mbox stream (mboxrd).</summary>
public static class MboxWriter
{
    // the keywords written as Status / X-Status letters
    private static readonly HashSet<string> Lettered = new(["$seen", "$answered", "$flagged", "$draft"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Appends one message: envelope line, state lines, the quoted message, a blank line.</summary>
    /// <param name="output">The mbox stream.</param>
    /// <param name="raw">The message as the server stores it.</param>
    /// <param name="sender">The envelope sender (the From address, or MAILER-DAEMON).</param>
    /// <param name="receivedAt">The envelope time.</param>
    /// <param name="keywords">The message's keywords ($seen, $flagged, $answered become the Status and X-Status letters; the others X-Keywords).</param>
    /// <param name="ct">Cancels the write.</param>
    public static async Task AppendAsync(Stream output, ReadOnlyMemory<byte> raw, string? sender, DateTimeOffset receivedAt, IReadOnlyCollection<string> keywords, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(keywords);
        var head = new StringBuilder()
            .Append("From ").Append(string.IsNullOrWhiteSpace(sender) || sender.Any(char.IsWhiteSpace) ? "MAILER-DAEMON" : sender).Append(' ')
            .Append(receivedAt.UtcDateTime.ToString("ddd MMM ", CultureInfo.InvariantCulture))
            .Append(receivedAt.UtcDateTime.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2))
            .Append(receivedAt.UtcDateTime.ToString(" HH:mm:ss yyyy", CultureInfo.InvariantCulture)).Append('\n');
        var has = new HashSet<string>(keywords, StringComparer.OrdinalIgnoreCase);
        head.Append("Status: ").Append(has.Contains("$seen") ? "RO" : "O").Append('\n');
        var x = (has.Contains("$answered") ? "A" : "") + (has.Contains("$flagged") ? "F" : "") + (has.Contains("$draft") ? "T" : "");
        if (x.Length > 0)
        {
            head.Append("X-Status: ").Append(x).Append('\n');
        }

        var other = keywords.Where(k => !Lettered.Contains(k)).ToList();
        if (other.Count > 0)
        {
            head.Append("X-Keywords: ").Append(string.Join(' ', other)).Append('\n');
        }

        await output.WriteAsync(Encoding.UTF8.GetBytes(head.ToString()), ct).ConfigureAwait(false);
        await output.WriteAsync(Quote(raw.Span), ct).ConfigureAwait(false);
        await output.WriteAsync("\n"u8.ToArray(), ct).ConfigureAwait(false);
    }

    /// <summary>The message with LF line ends, every ^>*From  line given one more ">", ending in a line end.</summary>
    public static byte[] Quote(ReadOnlySpan<byte> raw)
    {
        using var output = new MemoryStream(raw.Length + 64);
        var start = 0;
        while (start < raw.Length)
        {
            var end = raw[start..].IndexOf((byte)'\n');
            var line = end < 0 ? raw[start..] : raw.Slice(start, end);
            start = end < 0 ? raw.Length : start + end + 1;
            if (line.Length > 0 && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            var quotes = 0;
            while (quotes < line.Length && line[quotes] == (byte)'>')
            {
                quotes++;
            }

            if (line[quotes..].StartsWith("From "u8))
            {
                output.WriteByte((byte)'>');
            }

            output.Write(line);
            output.WriteByte((byte)'\n');
        }

        return output.ToArray();
    }
}
