// Mirrors: Bulwark lib/reply-recipients.ts buildReplyRecipients (Reply-To honoured, own addresses removed with the +tag stripped, self-sent #703 replies to the original recipients), ownReplyAddresses; Rust compose.rs:473-488 (the reply-all table).
// Duplicates across To and Cc are folded case-insensitively (the plan's table: "own address removed, Reply-To honoured, duplicates folded").

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Compose;

/// <summary>The To and Cc a reply or reply-all opens with.</summary>
public static class ReplyRecipients
{
    /// <summary>The recipients for <paramref name="message"/>: <paramref name="all"/> true for reply-all; <paramref name="own"/> are the user's addresses (identities and login).</summary>
    public static (IReadOnlyList<EmailAddress> To, IReadOnlyList<EmailAddress> Cc) Build(EmailBody message, bool all, IReadOnlyList<string> own, string subAddressDelimiter = "+")
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(own);
        static List<EmailAddress> WithEmail(IReadOnlyList<EmailAddress>? l) => [.. (l ?? []).Where(a => !string.IsNullOrWhiteSpace(a.Email))];
        var replyTo = WithEmail(message.ReplyTo);
        var externalReplyTo = replyTo.Where(r => !IsOwn(r.Email, own, subAddressDelimiter)).ToList();
        var from = message.From is { Count: > 0 } f ? f[0] : null;
        if (externalReplyTo.Count == 0 && from is not null && IsOwn(from.Email, own, subAddressDelimiter) && WithEmail(message.To) is { Count: > 0 } originalTo)
        {
            // replying to your own message continues the conversation, not mail to yourself; the recipients were the user's own choice (kept verbatim)
            return (Fold(originalTo, []), all ? Fold(WithEmail(message.Cc), originalTo) : []);
        }

        var target = replyTo.Count > 0 ? replyTo : from is { Email.Length: > 0 } ? [from] : [];
        if (!all)
        {
            return (Fold(target, []), []);
        }

        List<EmailAddress> Others(IReadOnlyList<EmailAddress>? l) => [.. WithEmail(l).Where(r => !IsOwn(r.Email, own, subAddressDelimiter))];
        var to = Fold([.. target, .. Others(message.To)], []);
        return (to, Fold(Others(message.Cc), to));
    }

    /// <summary>Whether <paramref name="email"/> is one of the user's own addresses: exactly, then without the sub-address tag.</summary>
    public static bool IsOwn(string? email, IReadOnlyList<string> own, string subAddressDelimiter = "+")
    {
        ArgumentNullException.ThrowIfNull(own);
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var exact = email.Trim().ToLowerInvariant();
        if (own.Any(o => string.Equals(o.Trim(), exact, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var b = Base(email, subAddressDelimiter);
        return own.Any(o => string.Equals(Base(o, subAddressDelimiter), b, StringComparison.Ordinal));
    }

    private static string Base(string email, string delimiter)
    {
        var e = email.Trim().ToLowerInvariant();
        var at = e.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || delimiter.Length == 0)
        {
            return e;
        }

        var local = e[..at];
        var cut = local.IndexOf(delimiter, StringComparison.Ordinal);
        return (cut >= 0 ? local[..cut] : local) + e[at..];
    }

    private static List<EmailAddress> Fold(IReadOnlyList<EmailAddress> list, IReadOnlyList<EmailAddress> seenBefore)
    {
        var seen = new HashSet<string>(seenBefore.Select(a => a.Email.Trim()), StringComparer.OrdinalIgnoreCase);
        var output = new List<EmailAddress>();
        foreach (var a in list)
        {
            if (seen.Add(a.Email.Trim()))
            {
                output.Add(a);
            }
        }

        return output;
    }
}
