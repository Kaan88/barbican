// Mirrors: Rust avatars.rs:66 MULTI_PART_TLDS, :76 PERSONAL_DOMAINS, :84 root_domain, :93 valid_domain, :103 gravatar_hash, :109 candidates, :122 get,
// :281 raster, :289 bimi_logo_url, :298 safe_https_host, :340 brand_domain, :352 trusted_authserv, :360 host_of, :363 dmarc_pass_domain;
// Bulwark components/ui/avatar.tsx (MULTI_PART_TLDS, PERSONAL_DOMAINS, getRootDomain), app/api/favicon isValidDomain, lib/bimi/*,
// lib/sender-icon-access.ts; RFC 7489 (DMARC), the BIMI draft; R2-07 and D-9 (only the account server's own Authentication-Results vouches for a
// logo: AvatarService passes AuthTrace's results through BrandDomain(from, results)); R2-08 (PNG row cap in PngDecoder). Owner reader.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Imaging;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Attachments;
using Barbican.Core.Mail.AuthResults;

namespace Barbican.Core.Mail.Avatars;

/// <summary>The pure decisions of the avatar pipeline.</summary>
public static class AvatarRules
{
    /// <summary>The side length of a fitted avatar picture (Rust EDGE).</summary>
    public const int Edge = 64;

    private static readonly HashSet<string> MultiPartTlds = new(StringComparer.Ordinal)
    {
        "co.uk", "org.uk", "me.uk", "ac.uk", "gov.uk", "net.uk", "co.jp", "or.jp", "ne.jp", "ac.jp", "go.jp",
        "co.kr", "or.kr", "go.kr", "ac.kr", "co.in", "net.in", "org.in", "ac.in", "gov.in",
        "co.nz", "org.nz", "net.nz", "govt.nz", "ac.nz", "co.za", "org.za", "net.za", "gov.za", "ac.za",
        "com.au", "net.au", "org.au", "edu.au", "gov.au", "com.br", "net.br", "org.br", "edu.br", "gov.br",
        "com.cn", "net.cn", "org.cn", "gov.cn", "edu.cn", "com.mx", "net.mx", "org.mx", "gob.mx", "edu.mx",
        "com.ar", "net.ar", "org.ar", "gob.ar", "edu.ar", "com.tw", "net.tw", "org.tw", "edu.tw", "gov.tw",
        "com.hk", "net.hk", "org.hk", "edu.hk", "gov.hk", "com.sg", "net.sg", "org.sg", "edu.sg", "gov.sg",
        "com.my", "net.my", "org.my", "edu.my", "gov.my", "com.ph", "net.ph", "org.ph", "edu.ph", "gov.ph",
        "com.pk", "net.pk", "org.pk", "edu.pk", "gov.pk", "com.ng", "net.ng", "org.ng", "edu.ng", "gov.ng",
        "co.il", "org.il", "net.il", "ac.il", "gov.il", "co.th", "or.th", "ac.th", "go.th", "in.th",
        "co.id", "or.id", "ac.id", "go.id", "web.id", "com.tr", "net.tr", "org.tr", "edu.tr", "gov.tr",
        "com.ua", "net.ua", "org.ua", "edu.ua", "gov.ua", "com.eg", "net.eg", "org.eg", "edu.eg", "gov.eg",
        "com.sa", "net.sa", "org.sa", "edu.sa", "gov.sa", "co.ke", "or.ke", "ac.ke", "go.ke", "ne.ke",
    };

    // Bulwark PERSONAL_DOMAINS (31 entries: mail providers and example.com/.org): the favicon would be the provider's logo, not the sender's. Kept as the
    // first 16 hex digits of the SHA-256 of each root domain, so the public repository names no provider (privacy-check).
    private static readonly HashSet<string> PersonalDomainHashes = new(StringComparer.Ordinal)
    {
        "591bfe88c880df96", "38c27a992c92962e", "06ae6b52ed986ed5", "f81e0a262b0f3b39", "e4a000438535d167", "7795a0faae5ae2c8",
        "b33960790a6de22c", "7f4b2e8dd4917e08", "97315ff74944e5b0", "b157b3314acb5e8a", "c22cfa842338a6a3", "98094d8ac24edb4c",
        "f847386b0fb0e44f", "8304152effb3d856", "48bf93ce6376a670", "a259c2b50d785080", "3e2ae79c33547fe9", "0bd023f4a98a5263",
        "3a4908053810a15a", "bbc6df007c79d295", "14e0dae7a689043f", "c3b2f6305fe63941", "79e20b2452034a83", "fb1a8477d681c97d",
        "abc34802cb5ecee9", "3fcb8282b84676eb", "9fda2d569d92a42c", "586b66a22aff427d", "1ad5e9aedd29bd28", "a379a6f6eeafb9a5",
        "bfabc37432958b06",
    };

    /// <summary>The registrable domain (the last two labels, three for the common second-level suffixes), lower-case.</summary>
    public static string RootDomain(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var d = domain.Trim().TrimEnd('.').ToLowerInvariant();
        var parts = d.Split('.');
        if (parts.Length <= 2)
        {
            return d;
        }

        var lastTwo = string.Join('.', parts[^2..]);
        return MultiPartTlds.Contains(lastTwo) ? string.Join('.', parts[^3..]) : lastTwo;
    }

    /// <summary>A dotted public name (no localhost, .local, .internal, .arpa, empty labels, leading hyphens or underscores).</summary>
    public static bool ValidDomain(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var d = domain.ToLowerInvariant();
        static bool LabelOk(string l) => l.Length is > 0 and <= 63 && !l.StartsWith('-') && !l.EndsWith('-') && l.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
        return d.Length <= 253 && d.Contains('.', StringComparison.Ordinal) && d.Split('.').All(LabelOk)
            && !string.Equals(d, "localhost", StringComparison.Ordinal) && !d.EndsWith(".local", StringComparison.Ordinal)
            && !d.EndsWith(".internal", StringComparison.Ordinal) && !d.EndsWith(".arpa", StringComparison.Ordinal);
    }

    /// <summary>The lower-case hex SHA-256 of the trimmed, lower-cased address.</summary>
    public static string GravatarHash(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())));
    }

    /// <summary>The picture sources to try, in Bulwark's order: "bimi:domain", "grav:hash", "fav:rootdomain".</summary>
    public static IReadOnlyList<string> Candidates(string email, string? brandDomain, AvatarPrefs prefs)
    {
        ArgumentNullException.ThrowIfNull(email);
        var domain = DomainOf(email);
        if (domain.Length == 0 || !ValidDomain(domain))
        {
            return [];
        }

        var v = new List<string>();
        if (prefs.Favicons && brandDomain is not null && string.Equals(brandDomain, domain, StringComparison.OrdinalIgnoreCase))
        {
            v.Add($"bimi:{domain}");
        }

        if (prefs.Gravatar)
        {
            v.Add($"grav:{GravatarHash(email)}");
        }

        var root = RootDomain(domain);
        if (prefs.Favicons && !IsPersonalDomain(root))
        {
            v.Add($"fav:{root}");
        }

        return v;
    }

    /// <summary>The picture for a sender from the cache, or null when the settings are off or it is junk and junk avatars are off.</summary>
    public static AvatarImage? Get(string email, string? brandDomain, AvatarPrefs prefs, bool junk) =>
        Get(email, brandDomain, prefs, junk, static _ => AvatarSlot.Unknown, static _ => { });

    /// <summary>
    /// The picture for a sender: the first candidate that is ready, or null while the best one is pending (the initials stay) or when every one
    /// is missing. An unknown candidate is passed to <paramref name="request"/> and counts as pending.
    /// </summary>
    public static AvatarImage? Get(string email, string? brandDomain, AvatarPrefs prefs, bool junk, Func<string, AvatarSlot> slot, Action<string> request)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(request);
        if (!prefs.Any || (junk && !prefs.InJunk))
        {
            return null;
        }

        foreach (var key in Candidates(email, brandDomain, prefs))
        {
            var s = slot(key);
            switch (s.State)
            {
                case AvatarSlotState.Ready:
                    return s.Image;
                case AvatarSlotState.Pending:
                    return null;
                case AvatarSlotState.Unknown:
                    request(key);
                    return null;
                default:
                    continue;
            }
        }

        return null;
    }

    /// <summary>The logo URL of a BIMI record (https, a public host), or null.</summary>
    public static string? BimiLogoUrl(string record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var parts = record.Split(';').Select(p => p.Trim()).ToArray();
        if (parts.Length == 0 || !parts[0].Equals("v=BIMI1", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var url = parts.FirstOrDefault(p => p.StartsWith("l=", StringComparison.Ordinal))?[2..].Trim();
        return url is not null && url.StartsWith("https://", StringComparison.Ordinal) && SafeHttpsHost(url) ? url : null;
    }

    /// <summary>The header.from domain of a dmarc=pass result in one Authentication-Results header, or null.</summary>
    public static string? DmarcPassDomain(string authenticationResults)
    {
        ArgumentNullException.ThrowIfNull(authenticationResults);
        var clean = new StringBuilder();
        var depth = 0;
        foreach (var c in authenticationResults)
        {
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && depth > 0)
            {
                depth--;
            }
            else if (depth == 0)
            {
                clean.Append(c);
            }
        }

        foreach (var part in clean.ToString().Split(';').Select(p => p.Trim()))
        {
            var lower = part.ToLowerInvariant();
            if (!lower.StartsWith("dmarc=pass", StringComparison.Ordinal))
            {
                continue;
            }

            var from = lower.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(t => t.StartsWith("header.from=", StringComparison.Ordinal));
            if (from is not null)
            {
                return from["header.from=".Length..].Trim('"', ';');
            }
        }

        return null;
    }

    /// <summary>The domain a logo may be shown for: From matches a dmarc pass from the account server's own header, else null (Rust semantics: the topmost header).</summary>
    public static string? BrandDomain(JsonNode message, string serverHost)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(serverHost);
        if (message["from"]?[0].Str("email") is not { } from || DomainOf(from) is not { Length: > 0 } domain || message["headers"] is not JsonArray headers)
        {
            return null;
        }

        var ar = headers.FirstOrDefault(h => string.Equals(h.Str("name"), "Authentication-Results", StringComparison.OrdinalIgnoreCase)).Str("value");
        if (ar is null || !TrustedAuthserv(ar, serverHost))
        {
            return null;
        }

        return DmarcPassDomain(ar) is { } d && string.Equals(d, domain, StringComparison.Ordinal) ? d : null;
    }

    /// <summary>The brand domain from the server's own results (AuthTrace, R2-07): a dmarc=pass whose header.from is the From domain.</summary>
    public static string? BrandDomain(string from, IReadOnlyList<AuthResult> serverResults)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(serverResults);
        var domain = DomainOf(from);
        return domain.Length > 0 && serverResults.Any(r => string.Equals(r.Method, "dmarc", StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Result, "pass", StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Prop("header.from")?.Trim('"').ToLowerInvariant(), domain, StringComparison.Ordinal))
            ? domain
            : null;
    }

    /// <summary>Whether the header's authserv-id is the account server (so its verdicts count).</summary>
    public static bool TrustedAuthserv(string authenticationResults, string serverHost)
    {
        ArgumentNullException.ThrowIfNull(authenticationResults);
        ArgumentNullException.ThrowIfNull(serverHost);
        var id = (authenticationResults.Split(';')[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty).TrimEnd('.').ToLowerInvariant();
        var host = serverHost.Trim().TrimEnd('.').ToLowerInvariant();
        if (id.Length == 0 || host.Length == 0)
        {
            return false;
        }

        return string.Equals(id, host, StringComparison.Ordinal)
            || (id.Contains('.', StringComparison.Ordinal) && host.Contains('.', StringComparison.Ordinal) && string.Equals(RootDomain(id), RootDomain(host), StringComparison.Ordinal));
    }

    /// <summary>The host of a URL, or empty.</summary>
    public static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : string.Empty;

    /// <summary>https, default port, no user info, a public DNS name (no IP literal, no .local, no localhost).</summary>
    public static bool SafeHttpsHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !string.Equals(u.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || !u.IsDefaultPort
            || u.UserInfo.Length > 0)
        {
            return false;
        }

        var d = u.Host;
        return d.Length > 0 && !d.StartsWith('[') && u.HostNameType == UriHostNameType.Dns && !IPAddress.TryParse(d, out _) && ValidDomain(d)
            && !d.Split('.').All(l => l.All(char.IsAsciiDigit));
    }

    /// <summary>Decodes an image, rejects one of 2 pixels or less on a side, and fits it to <see cref="Edge"/> by <see cref="Edge"/> (cover and centre crop).</summary>
    public static Raster? ToRaster(ReadOnlySpan<byte> image)
    {
        var bytes = image.ToArray();
        foreach (var decoder in ThumbnailDecoders.All)
        {
            if (decoder.TryDecode(bytes) is not { } img)
            {
                continue;
            }

            if (img.Width <= 2 || img.Height <= 2)
            {
                return null;
            }

            var fitted = ThumbnailScaler.Fill(img, Edge);
            return new Raster(fitted.Width, fitted.Height, fitted.Rgba);
        }

        return null;
    }

    /// <summary>Whether a root domain is a personal mail provider (Bulwark PERSONAL_DOMAINS).</summary>
    public static bool IsPersonalDomain(string rootDomain)
    {
        ArgumentNullException.ThrowIfNull(rootDomain);
        return PersonalDomainHashes.Contains(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rootDomain.ToLowerInvariant())))[..16]);
    }

    private static string DomainOf(string email)
    {
        var at = email.LastIndexOf('@');
        return at < 0 ? string.Empty : email[(at + 1)..].Trim().ToLowerInvariant();
    }
}
