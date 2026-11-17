// Mirrors: Rust msgload.rs (was_unseen :45, inline_bytes :50, PREFETCH_INLINE_MAX, mark_delay, threading; tag rust-final); Bulwark
// components/email/email-viewer.tsx markAsReadDelay (0 = at once, N ms, -1 = never). No-flicker rules in PORT-PLAN.md §4.5. Owner body.

using Barbican.Core.Mail.Model;
using Barbican.Core.Storage;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Small pure rules of loading a message for the reader.</summary>
public static class MessageLoadRules
{
    /// <summary>A prefetch skips (and does not cache) a message whose inline images add up to more (Rust PREFETCH_INLINE_MAX).</summary>
    public const long PrefetchInlineMax = 4_000_000;

    /// <summary>An inline image larger than this stays a <c>cid:</c> reference (Rust inline_cid_images).</summary>
    public const long InlineImageMax = 5_000_000;

    /// <summary>
    /// Whether a message was unread when it was opened, from the list's copy (the freshest) or the cached one; null when neither is known
    /// (decided from the loaded message). A null copy means "not an object" in Rust and is skipped.
    /// </summary>
    public static bool? WasUnseen(Keywords? listKeywords, Keywords? cachedKeywords) =>
        (listKeywords ?? cachedKeywords) is { } k ? !k.Seen : null;

    /// <summary>Total size of the inline images a message's body refers to (parts with a cid and an image type).</summary>
    public static long InlineBytes(EmailBody message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Attachments.Where(IsInlineImage).Sum(a => a.Size);
    }

    /// <summary>A part the HTML can show in place: it has a content id and an image type.</summary>
    public static bool IsInlineImage(Attachment a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return a.Cid is not null && (a.Type?.StartsWith("image/", StringComparison.Ordinal) ?? false);
    }

    /// <summary>Bulwark markAsReadDelay in ms: 0 = instantly, N = after N ms if still open, -1 = never (Rust mark_delay; default 0).</summary>
    public static long MarkAsReadDelay(PrefsSnapshot prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        return (long)prefs.Number("markAsReadDelay", 0);
    }

    /// <summary>Conversations are fetched and shown unless <c>disableThreading</c> is on (Rust threading).</summary>
    public static bool Threading(PrefsSnapshot prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        return !prefs.Bool("disableThreading", false);
    }
}
