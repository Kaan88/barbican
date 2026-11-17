// Mirrors: Bulwark lib/auth/mail-server-address.ts (emailDomain :14, UNSUPPORTED :25 and unsupportedProvider :37, normalizeServerInput :55,
// serverHostLabel :72) and its tests lib/auth/__tests__/mail-server-discovery.test.ts; spec I-098, I-101 (the unsupported-provider notice).
// Google's two domains are left out of the list: tools/privacy-check.sh refuses that provider's name anywhere in the repository (it guards a
// private address), so the notice does not cover it (FABLE-REVIEW "M1 stream: accounts"). IDN domains are not converted to ASCII here
// (Bulwark toAsciiDomain): a non-ASCII domain reads as not a domain yet.

using System.Text.RegularExpressions;

namespace Barbican.Core.Accounts;

/// <summary>What the sign-in form makes of what people type. No network.</summary>
public static partial class MailServerAddress
{
    private static readonly Dictionary<string, string> Unsupported = new(StringComparer.Ordinal)
    {
        ["outlook.com"] = "Outlook.com",
        ["hotmail.com"] = "Outlook.com",
        ["live.com"] = "Outlook.com",
        ["msn.com"] = "Outlook.com",
        ["icloud.com"] = "iCloud Mail",
        ["me.com"] = "iCloud Mail",
        ["mac.com"] = "iCloud Mail",
    };

    /// <summary>The domain of a complete email address, lower case, or "" while it is not one yet.</summary>
    public static string EmailDomain(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var trimmed = address.Trim().ToLowerInvariant();
        var at = trimmed.LastIndexOf('@');
        if (at <= 0)
        {
            return "";
        }

        var domain = trimmed[(at + 1)..].TrimEnd('.');
        return DomainRegex().IsMatch(domain) ? domain : "";
    }

    /// <summary>The provider's name when the domain belongs to one that offers no JMAP, else null.</summary>
    public static string? UnsupportedProvider(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var d = domain.ToLowerInvariant();
        if (Unsupported.TryGetValue(d, out var name))
        {
            return name;
        }

        // country variants: hotmail.de, outlook.fr, live.co.uk
        return d.Split('.')[0] switch
        {
            "hotmail" or "outlook" or "live" => "Outlook.com",
            _ => null,
        };
    }

    /// <summary>A server address as people type it, as a URL ("mail.example.org", "mail.example.org:8443", "https://…/"); null when it is not one.</summary>
    public static string? NormalizeServerInput(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var trimmed = input.Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace))
        {
            return null;
        }

        var withScheme = SchemeRegex().IsMatch(trimmed) ? trimmed : "https://" + trimmed;
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var url)
            || !(string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))
            || url.Host.Length == 0 || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0)
        {
            return null;
        }

        return url.GetLeftPart(UriPartial.Authority) + url.AbsolutePath.TrimEnd('/');
    }

    /// <summary>The host to show for a server URL: no scheme (except http://), no default port.</summary>
    public static string ServerHostLabel(string serverUrl)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var url))
        {
            return serverUrl;
        }

        var host = url.IsDefaultPort ? url.Host : $"{url.Host}:{url.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return (string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ? "http://" : "") + host + url.AbsolutePath.TrimEnd('/');
    }

    [GeneratedRegex("^(?=.{4,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z][a-z0-9-]{0,61}[a-z0-9]$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex DomainRegex();

    [GeneratedRegex("^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex SchemeRegex();
}
