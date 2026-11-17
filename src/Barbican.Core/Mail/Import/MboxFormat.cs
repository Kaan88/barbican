// Mirrors: Rust mailimport.rs:55-170 (is_empty_line, strip_eol, is_envelope_line :66, split_mbox :85, header_fields :93, mbox_state :114, received_at :148,
// to_item :155, looks_mbox :161, items_of :169), :232 import_entry, :268 message_id, :381 summary; RFC 4155 (the mbox format, mboxrd quoting), RFC 5322
// section 3.3 (Date) and 3.6.4 (Message-ID), RFC 8621 section 4.8 (Email/import); Bulwark lib/mail-import/{mbox,mbox-status,engine}.ts (isEnvelopeLine,
// mboxState). Owner unified (M2 wave 2).

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.KeywordSyntax;

namespace Barbican.Core.Mail.Import;

/// <summary>The pure parts of reading mbox and .eml files.</summary>
public static class MboxFormat
{
    private static readonly string[] IgnoredKeywords = ["$recent", "\\recent", "$hasattachment", "$hasnoattachment"];

    /// <summary>"From sender Tue May  5 10:11:12 2020" (a zone before the year, or "remote from host" after it): an envelope line, not a body line.</summary>
    public static bool IsEnvelopeLine(ReadOnlySpan<byte> line)
    {
        var t = Encoding.UTF8.GetString(StripEol(line));
        if (!t.StartsWith("From ", StringComparison.Ordinal))
        {
            return false;
        }

        var words = t[5..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count < 3)
        {
            return false;
        }

        if (string.Equals(words[^3], "remote", StringComparison.Ordinal) && string.Equals(words[^2], "from", StringComparison.Ordinal))
        {
            words.RemoveRange(words.Count - 3, 3);
        }

        if (words.Count == 0)
        {
            return false;
        }

        var year = words[^1];
        if (year.Length != 4 || !year.All(char.IsAsciiDigit))
        {
            return false;
        }

        // a time hh:mm(:ss) among the words after the sender
        return words.Skip(1).Any(w =>
        {
            var p = w.Split(':');
            return p.Length is 2 or 3 && p[0].Length <= 2 && p.All(x => x.Length > 0 && x.All(char.IsAsciiDigit));
        });
    }

    /// <summary>Splits an mbox file into messages (mboxo and mboxrd): a separator is an envelope line at the start or after an empty line (that empty line is dropped); >From lines lose one >.</summary>
    public static IReadOnlyList<byte[]> Split(ReadOnlySpan<byte> mbox)
    {
        var splitter = new MboxSplitter();
        var output = new List<byte[]>();
        var start = 0;
        for (var i = 0; i < mbox.Length; i++)
        {
            if (mbox[i] == (byte)'\n')
            {
                if (splitter.Push(mbox[start..(i + 1)]) is { } m)
                {
                    output.Add(m);
                }

                start = i + 1;
            }
        }

        if (start < mbox.Length && splitter.Push(mbox[start..]) is { } last)
        {
            output.Add(last);
        }

        if (splitter.Finish() is { } tail)
        {
            output.Add(tail);
        }

        return output;
    }

    /// <summary>The header block of a message, unfolded, by lower-cased name.</summary>
    public static IReadOnlyList<(string Name, string Value)> HeaderFields(ReadOnlySpan<byte> message)
    {
        var text = Encoding.UTF8.GetString(message);
        var crlf = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var head = crlf < 0 ? text : text[..crlf];
        var lf = head.IndexOf("\n\n", StringComparison.Ordinal);
        head = lf < 0 ? head : head[..lf];
        var output = new List<(string Name, string Value)>();
        foreach (var line in head.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith(' ') || line.StartsWith('\t'))
            {
                if (output.Count > 0)
                {
                    var last = output[^1];
                    output[^1] = (last.Name, last.Value + " " + line.Trim());
                }

                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            var name = line[..colon];
            if (name.Trim().Length > 0 && !name.Contains(' ', StringComparison.Ordinal))
            {
                output.Add((name.Trim().ToLowerInvariant(), line[(colon + 1)..].Trim()));
            }
        }

        return output;
    }

    /// <summary>Keywords from the status headers mail programs write (Status, X-Status, X-Mozilla-Status, the labels header, X-Keywords), and whether the message was deleted; with none of them it is imported as read.</summary>
    public static (IReadOnlyList<string> Keywords, bool Expunged) State(IReadOnlyList<(string Name, string Value)> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var status = Field(fields, "status");
        var xstatus = Field(fields, "x-status");
        var moz = Field(fields, "x-mozilla-status");

        // the labels header of one large webmail provider's export (Bulwark mbox-status.ts)
        var labels = Field(fields, "x-g" + "mail-labels");
        var kw = new List<string>();
        var expunged = false;
        Add(kw, "$seen", status is null && xstatus is null && moz is null && labels is null);
        Add(kw, "$seen", status is not null && status.ToUpperInvariant().Contains('R', StringComparison.Ordinal));
        if (xstatus?.ToUpperInvariant() is { } x)
        {
            Add(kw, "$answered", x.Contains('A', StringComparison.Ordinal));
            Add(kw, "$flagged", x.Contains('F', StringComparison.Ordinal));
            Add(kw, "$draft", x.Contains('T', StringComparison.Ordinal));
        }

        if (moz is { Length: <= 8 } && uint.TryParse(moz, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var flags))
        {
            Add(kw, "$seen", (flags & 0x1) != 0);
            Add(kw, "$answered", (flags & 0x2) != 0);
            Add(kw, "$flagged", (flags & 0x4) != 0);
            expunged = (flags & 0x8) != 0;
            Add(kw, "$forwarded", (flags & 0x1000) != 0);
        }

        if (labels is not null)
        {
            var l = labels.Split(',').Select(s => s.Trim().ToLowerInvariant()).ToList();
            Add(kw, "$seen", !l.Contains("unread", StringComparer.Ordinal));
            Add(kw, "$flagged", l.Contains("starred", StringComparer.Ordinal));
        }

        foreach (var v in new[] { Field(fields, "x-keywords"), Field(fields, "x-mozilla-keys") })
        {
            foreach (var raw in (v ?? string.Empty).Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                var k = raw.ToLowerInvariant() switch
                {
                    "\\seen" => "$seen",
                    "\\flagged" => "$flagged",
                    "\\answered" => "$answered",
                    "\\draft" => "$draft",
                    var o => o,
                };
                Add(kw, k, KeywordRules.IsValid(k) && !IgnoredKeywords.Contains(k, StringComparer.Ordinal));
            }
        }

        return (kw, expunged);
    }

    /// <summary>The arrival time from the Date header (RFC 5322) as RFC 3339 UTC, or null.</summary>
    public static string? ReceivedAt(IReadOnlyList<(string Name, string Value)> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (Field(fields, "date") is not { } d)
        {
            return null;
        }

        // drop a trailing comment such as "(UTC)"
        var cut = d.IndexOf(" (", StringComparison.Ordinal);
        d = (cut < 0 ? d : d[..cut]).Trim();
        return Rfc5322Date.TryParse(d, out var t) ? t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : null;
    }

    /// <summary>The messages of one file: an .mbox (or anything starting with an envelope line) is split, anything else is one message; deleted messages are left out.</summary>
    public static IReadOnlyList<ImportItem> ItemsOf(string fileName, ReadOnlySpan<byte> content)
    {
        var reader = new MboxReader(fileName, new MemoryStream(content.ToArray(), writable: false), long.MaxValue);
        var output = new List<ImportItem>();
        while (reader.NextItem() is { } item)
        {
            output.Add(item);
        }

        return output;
    }

    /// <summary>The Email/import entry for an uploaded message: blobId, mailboxIds, keywords, receivedAt when known.</summary>
    public static JsonObject ImportEntry(string blobId, string mailboxId, ImportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var keywords = new JsonObject();
        foreach (var k in item.Keywords)
        {
            keywords[k] = true;
        }

        var e = new JsonObject
        {
            ["blobId"] = blobId,
            ["mailboxIds"] = new JsonObject { [mailboxId] = true },
            ["keywords"] = keywords,
        };
        if (item.ReceivedAt is { } r)
        {
            e["receivedAt"] = r;
        }

        return e;
    }

    /// <summary>The Message-ID of a raw message without angle brackets, or null.</summary>
    public static string? MessageId(ReadOnlySpan<byte> message)
    {
        if (Field(HeaderFields(message), "message-id") is not { } v)
        {
            return null;
        }

        v = v.Trim().TrimStart('<').TrimEnd('>').Trim();
        return v.Length > 0 && v.IndexOfAny(['"', '\\']) < 0 ? v : null;
    }

    /// <summary>"Imported 2 messages, 3 already in the folder" and "Imported 3 messages, 1 not imported: reason".</summary>
    public static string Summary(int imported, int already, int failed, string? firstError)
    {
        static string N(int k) => k == 1 ? "1 message" : $"{k} messages";
        var s = new StringBuilder("Imported ").Append(N(imported));
        if (already > 0)
        {
            s.Append(CultureInfo.InvariantCulture, $", {already} already in the folder");
        }

        if (failed > 0)
        {
            s.Append(CultureInfo.InvariantCulture, $", {failed} not imported");
            if (firstError is not null)
            {
                s.Append(": ").Append(firstError);
            }
        }

        return s.ToString();
    }

    /// <summary>Whether a file is read as an mbox: by its extension, or by an envelope line at its start (Rust mailimport.rs:161 looks_mbox).</summary>
    internal static bool LooksMbox(string fileName, ReadOnlySpan<byte> firstLine)
    {
        var lower = fileName.ToLowerInvariant();
        return lower.EndsWith(".mbox", StringComparison.Ordinal) || lower.EndsWith(".mbx", StringComparison.Ordinal) || lower.EndsWith(".mbs", StringComparison.Ordinal)
            || (firstLine.StartsWith("From "u8) && IsEnvelopeLine(firstLine));
    }

    /// <summary>A raw message as an import item, or null when the mbox marked it deleted (Rust mailimport.rs:155 to_item).</summary>
    internal static ImportItem? ToItem(byte[] message)
    {
        var f = HeaderFields(message);
        var (keywords, expunged) = State(f);
        return expunged ? null : new ImportItem(message, keywords, ReceivedAt(f));
    }

    internal static bool IsEmptyLine(ReadOnlySpan<byte> line) => line.SequenceEqual("\n"u8) || line.SequenceEqual("\r\n"u8);

    internal static bool Blank(ReadOnlySpan<byte> message)
    {
        foreach (var b in message)
        {
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or 0x0c))
            {
                return false;
            }
        }

        return true;
    }

    private static ReadOnlySpan<byte> StripEol(ReadOnlySpan<byte> line)
    {
        var e = line.Length;
        if (e > 0 && line[e - 1] == (byte)'\n')
        {
            e--;
        }

        if (e > 0 && line[e - 1] == (byte)'\r')
        {
            e--;
        }

        return line[..e];
    }

    private static void Add(List<string> keywords, string keyword, bool when)
    {
        keyword = keyword.ToLowerInvariant();
        if (when && !keywords.Contains(keyword, StringComparer.Ordinal))
        {
            keywords.Add(keyword);
        }
    }

    private static string? Field(IReadOnlyList<(string Name, string Value)> fields, string name)
    {
        foreach (var (n, v) in fields)
        {
            if (string.Equals(n, name, StringComparison.Ordinal))
            {
                return v;
            }
        }

        return null;
    }
}
