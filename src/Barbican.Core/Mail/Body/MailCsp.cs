// Mirrors: Rust main.rs:89 csp_with (tag rust-final) and its test main.rs:106
//   (T-176 main::blocked_remote_content_includes_stylesheets_and_fonts).
// Bulwark: lib/email-sanitization.ts:109-118 emailIframeCsp (default-src 'none', data:-only images and
//   fonts while external content is blocked, base-uri 'none', form-action 'none', frame-src 'none').
//   Differences kept from Rust: remote stylesheets are allowed with remote content on (Bulwark keeps
//   style-src inline-only), media-src stays 'none' (Bulwark allows data:/blob:), no blob: (cid: parts
//   become data: URIs, M1-plan §6.5). Taken from Bulwark: base-uri 'none' (Rust had none).
// W3C CSP Level 3 §6 (directives; the default-src fallback covers media-src and the rest).
// Stalwart: none (client-side only).
// Docs: docs/adr/0005 decision 4 and "M0 review": on Linux (WebKitGTK, no WebResourceRequested) this
//   policy is the ONLY remote-content guard, so it is built here, once, for both OSes.
using System.Security.Cryptography;
using System.Text;

namespace Barbican.Core.Mail.Body;

/// <summary>
/// The Content-Security-Policy of one message frame. With remote content blocked a remote
/// stylesheet, font or image is a tracking beacon, so style-src, font-src and img-src allow only
/// inline styles and <c>data:</c>; media, objects, frames and form targets fall back to
/// <c>default-src 'none'</c> or are named <c>'none'</c>; a mail's own <c>&lt;base href&gt;</c> is ignored. Scripts never run, except our own script by
/// its SHA-256 hash (the dark pass); never <c>'unsafe-inline'</c>.
/// </summary>
public static class MailCsp
{
    /// <summary>The policy text (the <c>content</c> of the meta tag).</summary>
    public static string Policy(bool remoteContent, string? script)
    {
        var scripts = script is null ? "'none'" : ScriptSource(script);
        var (style, font, img) = remoteContent
            ? ("'unsafe-inline' http: https:", "http: https: data:", "http: https: data:")
            : ("'unsafe-inline'", "data:", "data:");
        return $"default-src 'none'; style-src {style}; font-src {font}; img-src {img}; script-src {scripts}; object-src 'none'; frame-src 'none'; form-action 'none'; base-uri 'none'";
    }

    /// <summary>The meta tag the frame document starts with (Rust <c>csp_with</c>).</summary>
    public static string MetaTag(bool remoteContent, string? script) =>
        $"<meta http-equiv=\"Content-Security-Policy\" content=\"{Policy(remoteContent, script)}\">";

    /// <summary>The CSP source that allows exactly <paramref name="script"/>: <c>'sha256-BASE64'</c> of its UTF-8 bytes.</summary>
    public static string ScriptSource(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        return $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)))}'";
    }
}
