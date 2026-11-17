// Mirrors: Rust avatars.rs:84 root_domain, :93 valid_domain, :103 gravatar_hash, :109 candidates, :122 get, :281 raster, :289 bimi_logo_url, :298 safe_https_host,
// :340 brand_domain, :352 trusted_authserv, :360 host_of, :363 dmarc_pass_domain; Bulwark lib/bimi/*, lib/root-domain.ts, lib/sender-icon-access.ts;
// RFC 7489 (DMARC), the BIMI draft; R2-07 and D-9 (only the account server's own Authentication-Results vouches for a logo); R2-08 (PNG row cap).
// Stub from stage F of M2; owner reader.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Imaging;

namespace Barbican.Core.Mail.Avatars;

/// <summary>The pure decisions of the avatar pipeline.</summary>
public static class AvatarRules
{
    /// <summary>The side length of a fitted avatar picture (Rust EDGE).</summary>
    public const int Edge = 64;

    /// <summary>The registrable domain (the last two labels, three for the common second-level suffixes), lower-case.</summary>
    public static string RootDomain(string domain)
    {
        throw new PortPendingException("avatars.rs:84 root_domain");
    }

    /// <summary>A dotted public name (no localhost, .local, empty labels, leading hyphens or underscores).</summary>
    public static bool ValidDomain(string domain)
    {
        throw new PortPendingException("avatars.rs:93 valid_domain");
    }

    /// <summary>The lower-case hex SHA-256 of the trimmed, lower-cased address.</summary>
    public static string GravatarHash(string email)
    {
        throw new PortPendingException("avatars.rs:103 gravatar_hash");
    }

    /// <summary>The picture sources to try, in Bulwark's order: "bimi:domain", "grav:hash", "fav:rootdomain".</summary>
    public static IReadOnlyList<string> Candidates(string email, string? brandDomain, AvatarPrefs prefs)
    {
        throw new PortPendingException("avatars.rs:109 candidates");
    }

    /// <summary>The picture for a sender from the cache, or null when the settings are off or it is junk and junk avatars are off.</summary>
    public static AvatarImage? Get(string email, string? brandDomain, AvatarPrefs prefs, bool junk)
    {
        throw new PortPendingException("avatars.rs:122 get");
    }

    /// <summary>The logo URL of a BIMI record (https, a public host), or null.</summary>
    public static string? BimiLogoUrl(string record)
    {
        throw new PortPendingException("avatars.rs:289 bimi_logo_url");
    }

    /// <summary>The header.from domain of a dmarc=pass result in one Authentication-Results header, or null.</summary>
    public static string? DmarcPassDomain(string authenticationResults)
    {
        throw new PortPendingException("avatars.rs:363 dmarc_pass_domain");
    }

    /// <summary>The domain a logo may be shown for: From matches a dmarc pass from the account server's own header, else null.</summary>
    public static string? BrandDomain(JsonNode message, string serverHost)
    {
        throw new PortPendingException("avatars.rs:340 brand_domain");
    }

    /// <summary>Whether the header's authserv-id is the account server (so its verdicts count).</summary>
    public static bool TrustedAuthserv(string authenticationResults, string serverHost)
    {
        throw new PortPendingException("avatars.rs:352 trusted_authserv");
    }

    /// <summary>The host of a URL, or empty.</summary>
    public static string HostOf(string url)
    {
        throw new PortPendingException("avatars.rs:360 host_of");
    }

    /// <summary>https, default port, no user info, a public DNS name (no IP literal, no .local, no localhost).</summary>
    public static bool SafeHttpsHost(string url)
    {
        throw new PortPendingException("avatars.rs:298 safe_https_host");
    }

    /// <summary>Decodes an image, rejects one of 2 pixels or less on a side, and fits it to <see cref="Edge"/> by <see cref="Edge"/> (cover and centre crop).</summary>
    public static Raster? ToRaster(ReadOnlySpan<byte> image)
    {
        throw new PortPendingException("avatars.rs:281 raster");
    }
}
