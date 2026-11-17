// Mirrors: Rust jmap.rs:1207 header_from, :1221 wildcard_covers, :1232 choose_identity, :1243 submission_envelope, :1251 mail_from_refused;
// Bulwark lib/jmap/client.ts (identities, the "Edit sender" From override and the catch-all reply rewrite); RFC 8621 §6 (Identity),
// §7 (EmailSubmission envelope), §7.5 (forbiddenMailFrom; Stalwart's forbiddenFrom). M3 sends with them; M1 ports and tests them. Owner jmap.

using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Jmap.Submission;

/// <summary>The pure From and envelope rules of sending.</summary>
public static class SubmissionRules
{
    /// <summary>The header From: the override when it has an address, else the identity (a wildcard identity names the login).</summary>
    public static EmailAddress HeaderFrom(Outgoing outgoing, Identity identity, string username)
    {
        ArgumentNullException.ThrowIfNull(outgoing);
        ArgumentNullException.ThrowIfNull(identity);
        if (outgoing.FromOverride is { } o && o.Email.Trim().Contains('@', StringComparison.Ordinal))
        {
            var name = o.Name.Trim();
            return new EmailAddress(name.Length == 0 ? null : name, o.Email.Trim());
        }

        var e = identity.Email;
        var email = e.Contains('*', StringComparison.Ordinal) || !e.Contains('@', StringComparison.Ordinal) ? username : e;
        return new EmailAddress(string.IsNullOrWhiteSpace(identity.Name) ? null : identity.Name, email);
    }

    /// <summary>True when a <c>*@domain</c> identity covers the address (case-insensitive, non-empty local part).</summary>
    public static bool WildcardCovers(string pattern, string address)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(address);
        var p = pattern.Trim().ToLowerInvariant();
        var a = address.Trim().ToLowerInvariant();
        var pi = p.IndexOf('@', StringComparison.Ordinal);
        var ai = a.IndexOf('@', StringComparison.Ordinal);
        return pi >= 0 && ai > 0 && string.Equals(p[..pi], "*", StringComparison.Ordinal)
            && string.Equals(p[(pi + 1)..], a[(ai + 1)..], StringComparison.Ordinal);
    }

    /// <summary>
    /// The identity to send through: the override's own identity, the composer's (by address or id), one covering the override as a
    /// wildcard, else the first; null when an override with an address matches nothing (it must not go out under an unrelated identity).
    /// </summary>
    public static Identity? ChooseIdentity(IReadOnlyList<Identity> identities, string from, string? fromOverride)
    {
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(from);
        static bool Same(Identity i, string a)
        {
            var t = a.Trim();
            return t.Length > 0 && string.Equals(i.Email.Trim(), t, StringComparison.OrdinalIgnoreCase);
        }

        return (fromOverride is null ? null : identities.FirstOrDefault(i => Same(i, fromOverride)))
            ?? identities.FirstOrDefault(i => Same(i, from))
            ?? identities.FirstOrDefault(i => string.Equals(i.Id, from, StringComparison.Ordinal))
            ?? (fromOverride is null ? null : identities.FirstOrDefault(i => WildcardCovers(i.Email, fromOverride)))
            ?? (fromOverride is not null && fromOverride.Contains('@', StringComparison.Ordinal) ? null : identities.Count > 0 ? identities[0] : null);
    }

    /// <summary>A custom envelope when MAIL FROM differs from the identity's or a hold is set; null when the server may derive it.</summary>
    public static SubmissionEnvelope? Envelope(string mailFrom, string identityMailFrom, long? holdSeconds, IReadOnlyList<string> recipients)
    {
        ArgumentNullException.ThrowIfNull(mailFrom);
        ArgumentNullException.ThrowIfNull(recipients);
        if (holdSeconds is null && string.Equals(mailFrom, identityMailFrom, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parameters = holdSeconds is { } h
            ? new Dictionary<string, string>(StringComparer.Ordinal) { ["HOLDFOR"] = h.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            : null;
        return new SubmissionEnvelope(new EnvelopeAddress(mailFrom, parameters), [.. recipients.Select(r => new EnvelopeAddress(r))]);
    }

    /// <summary>True for <c>forbiddenFrom</c> and <c>forbiddenMailFrom</c>.</summary>
    public static bool MailFromRefused(SetError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Type is "forbiddenMailFrom" or "forbiddenFrom";
    }
}
