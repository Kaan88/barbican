// Mirrors: Rust mailto.rs:33 decode_lenient, :62 parse, :115 keep_pending, :121 sign_in_message, limits :20-22; Rust mailto.rs (tag rust-final); RFC 6068 (mailto URI); Bulwark app/[locale]/page.tsx mailto handling, lib/mailto.ts. Contract stub from the M3 plan section 4.5; owner composer-ui replaces the bodies.

using Barbican.Core.Common;

namespace Barbican.Core.Mailto;

/// <summary>RFC 6068 parsing with the desktop app's limits (stub until stream composer-ui lands).</summary>
public static class MailtoLink
{
    public const int MaxRecipients = 200;

    public const int MaxSubject = 998;

    public const int MaxBody = 64 * 1024;

    /// <summary>Links kept while no account is signed in (the oldest are dropped).</summary>
    public const int MaxPending = 5;

    /// <summary>Percent-decoding that never fails: a stray percent stays, broken UTF-8 becomes U+FFFD.</summary>
    public static string DecodeLenient(string value) => throw new PortPendingException("MailtoLink.DecodeLenient");

    /// <summary>
    /// A mailto: link (scheme case-insensitive) as a <see cref="MailtoFields"/>; null for anything else. Recipients split on commas (quoted names respected), <c>to</c>/<c>cc</c>/<c>bcc</c> parameters in any case, repeated
    /// parameters appended, the fragment ignored (an encoded # stays), control characters dropped from the subject and recipients, CRLF in the body normalised to LF, at most <see cref="MaxRecipients"/> recipients in all (Cc and Bcc get what To leaves, so a long To empties them), <see cref="MaxSubject"/> and <see cref="MaxBody"/> limits. A plus sign stays a plus.
    /// </summary>
    public static MailtoFields? Parse(string raw) => throw new PortPendingException("MailtoLink.Parse");

    /// <summary>Appends a link to the pending list, keeping the newest <see cref="MaxPending"/>.</summary>
    public static void KeepPending(IList<MailtoFields> list, MailtoFields link) => throw new PortPendingException("MailtoLink.KeepPending");

    /// <summary>"Sign in to write to a@x.example." (the first recipient; "Sign in to write this message." without one).</summary>
    public static string SignInMessage(MailtoFields link) => throw new PortPendingException("MailtoLink.SignInMessage");
}
