// Mirrors: Rust reader.rs:19 SOURCE_DISPLAY_LIMIT, :247 displayed_truncated, :252 source_shown; Bulwark components/email/view-source-dialog.tsx,
// lib/email-source.ts; G-056 (fetch the full body of a truncated message). Stub from stage F of M2; owner reader.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Mail.Reader;

/// <summary>Truncated bodies and the capped view-source text.</summary>
public static class SourceCut
{
    /// <summary>More than this many bytes is cut in the source view (the whole message is one Save away): 2 MiB.</summary>
    public const int DisplayLimit = 2 * 1024 * 1024;

    /// <summary>Whether the displayed body part was cut off by maxBodyValueBytes.</summary>
    public static bool DisplayedTruncated(JsonNode message)
    {
        throw new PortPendingException("reader.rs:247 displayed_truncated");
    }

    /// <summary>The source as shown: at most <see cref="DisplayLimit"/> UTF-8 bytes, cut on a character boundary, and whether it was cut.</summary>
    public static (string Text, bool Cut) Shown(string text)
    {
        throw new PortPendingException("reader.rs:252 source_shown");
    }
}
