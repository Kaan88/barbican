// Mirrors: Bulwark lib/external-mail.ts:29-71 (bareAddress, addressDomain, domainMatches, internalDomains, isExternalAddress), :113-132
// (externalSenders), components/email/external-sender-chip.tsx:120-125 (the chip's tooltip: the distinct sender domains, or none);
// lib/admin/organisation-policy.ts:225-278 (normalizeDomainList: ASCII form, lower case, a domain covers its subdomains, 500 at most).
// Deviation (G-051): Bulwark reads the organisation policy's externalMail.senderLabel and its domains list; Barbican has no admin policy UI,
// so the chip is a user display setting (prefs externalSenderLabel) and only the identities' own domains count as internal.

using System.Text.RegularExpressions;
using Barbican.Core.Compose.Recipients;

namespace Barbican.Presentation.Reader;

/// <summary>Who is outside the organisation, for the reader's "External" sender chip.</summary>
public static class ExternalSenderRules
{
    private const int MaxDomains = 500;

    // Bulwark's DOMAIN_RE (organisation-policy.ts:229): a real domain has at least one dot, so "localhost" never counts as internal.
    private static readonly Regex Domain = new("^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | RegexOptions.ExplicitCapture);

    /// <summary>The bare address of "Name &lt;a@b&gt;" or "a@b".</summary>
    public static string BareAddress(string? address)
    {
        var raw = (address ?? string.Empty).Trim();
        var close = raw.LastIndexOf('>');
        if (close < 0 || raw[(close + 1)..].Trim().Length > 0)
        {
            return raw;
        }

        var open = raw.LastIndexOf('<', close);
        return open >= 0 ? raw[(open + 1)..close].Trim() : raw;
    }

    /// <summary>The domain of an address in lower-case ASCII form; empty without a usable one (external-mail.ts:36-42).</summary>
    public static string AddressDomain(string? address)
    {
        var bare = BareAddress(address);
        var at = bare.LastIndexOf('@');
        if (at < 0)
        {
            return string.Empty;
        }

        var domain = bare[(at + 1)..].Trim().TrimEnd('.');
        return domain.Length == 0 ? string.Empty : RecipientText.ToAsciiDomain(domain) ?? string.Empty;
    }

    /// <summary>
    /// The domains of the user's identities, in ASCII form and deduplicated, in the order they were given (external-mail.ts:61-64).
    /// Bulwark also appends the admin's list; Barbican has no organisation domains.
    /// </summary>
    public static IReadOnlyList<string> InternalDomains(IEnumerable<string?> identityEmails)
    {
        ArgumentNullException.ThrowIfNull(identityEmails);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var email in identityEmails)
        {
            var domain = AddressDomain(email);
            if (Domain.IsMatch(domain) && seen.Add(domain) && list.Count < MaxDomains)
            {
                list.Add(domain);
            }
        }

        return list;
    }

    /// <summary>True when <paramref name="domain"/> is one of <paramref name="internalDomains"/> or a subdomain of one (external-mail.ts:50-54).</summary>
    public static bool DomainMatches(string domain, IReadOnlyList<string> internalDomains)
    {
        if (string.IsNullOrEmpty(domain))
        {
            return false;
        }

        var d = domain.ToLowerInvariant();
        return internalDomains.Any(entry => string.Equals(d, entry, StringComparison.Ordinal) || d.EndsWith("." + entry, StringComparison.Ordinal));
    }

    /// <summary>
    /// The domains of the From addresses that are outside the organisation, one per external address (external-mail.ts:119-132); empty when
    /// nothing can be told apart. An address without a domain counts and brings an empty entry, as Bulwark does.
    /// </summary>
    public static IReadOnlyList<string> ExternalSenderDomains(IEnumerable<string?> from, IReadOnlyList<string> internalDomains)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(internalDomains);
        if (internalDomains.Count == 0)
        {
            return [];
        }

        var list = new List<string>();
        foreach (var address in from)
        {
            var domain = AddressDomain(address);
            if (domain.Length > 0 && DomainMatches(domain, internalDomains))
            {
                continue;
            }

            list.Add(domain);
        }

        return list;
    }

    /// <summary>The chip's tooltip: the distinct sender domains, or the no-domain wording (external-sender-chip.tsx:121-125; locales/en common.json:977-978).</summary>
    public static string Tooltip(IReadOnlyList<string> externalDomains)
    {
        ArgumentNullException.ThrowIfNull(externalDomains);
        var named = string.Join(", ", externalDomains.Where(d => d.Length > 0).Distinct(StringComparer.Ordinal));
        return named.Length > 0
            ? $"From {named}, outside your organisation"
            : "From an address without a domain, outside your organisation";
    }
}
