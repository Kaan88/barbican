// Mirrors: Rust reader.rs:19 SOURCE_DISPLAY_LIMIT, :247 displayed_truncated, :252 source_shown; Bulwark components/email/view-source-dialog.tsx,
// lib/email-source.ts (SOURCE_DISPLAY_LIMIT), hasTruncatedDisplayedBody; RFC 8621 §4.2 (isTruncated, maxBodyValueBytes); G-056 (fetch the full
// body of a truncated message).

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Mail.Reader;

/// <summary>Truncated bodies and the capped view-source text.</summary>
public static class SourceCut
{
    /// <summary>More than this many bytes is cut in the source view (the whole message is one Save away): 2 MiB.</summary>
    public const int DisplayLimit = 2 * 1024 * 1024;

    /// <summary>Whether the displayed body part was cut off by maxBodyValueBytes.</summary>
    public static bool DisplayedTruncated(JsonNode message)
    {
        ArgumentNullException.ThrowIfNull(message);
        foreach (var key in new[] { "htmlBody", "textBody" })
        {
            if (message[key] is JsonArray { Count: > 0 } parts && parts[0].Str("partId") is { } part
                && message["bodyValues"]?[part]?["isTruncated"] is JsonValue v && v.TryGetValue<bool>(out var cut) && cut)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The source as shown: at most <see cref="DisplayLimit"/> UTF-8 bytes, cut on a character boundary, and whether it was cut.</summary>
    public static (string Text, bool Cut) Shown(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Encoding.UTF8.GetByteCount(text) <= DisplayLimit)
        {
            return (text, false);
        }

        // whole characters only: count runes until the next one would pass the limit
        var bytes = 0;
        var chars = 0;
        foreach (var r in text.EnumerateRunes())
        {
            if (bytes + r.Utf8SequenceLength > DisplayLimit)
            {
                break;
            }

            bytes += r.Utf8SequenceLength;
            chars += r.Utf16SequenceLength;
        }

        return (text[..chars], true);
    }
}
