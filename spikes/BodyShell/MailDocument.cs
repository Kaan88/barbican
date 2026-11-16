using System.Security.Cryptography;
using System.Text;

namespace Barbican.Spike.BodyShell;

/// <summary>
/// The pages the shell shows, as the Rust app built them (main.rs csp_with/page at rust-final,
/// dark_mail.rs css). M1 ports this into Core with its Rust tests; here it only feeds the spike.
/// </summary>
internal static class MailDocument
{
    public const string DarkPendingClass = "barbican-dark-pending";
    public const string DarkBackground = "#0a0a0a";

    /// <summary>Frame sandbox: same origin (the shell sees when it is laid out), popups for links, modals for print; scripts only when the page carries our own hashed script.</summary>
    public static string Sandbox(bool script) => script
        ? "allow-same-origin allow-popups allow-popups-to-escape-sandbox allow-modals allow-scripts"
        : "allow-same-origin allow-popups allow-popups-to-escape-sandbox allow-modals";

    /// <summary>The message CSP. With remote content blocked a remote stylesheet or font is a beacon like an image; scripts only by hash.</summary>
    public static string Csp(bool images, string? script)
    {
        var scripts = script is null ? "'none'" : $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)))}'";
        var (style, font, img) = images
            ? ("'unsafe-inline' http: https:", "http: https: data:", "http: https: data:")
            : ("'unsafe-inline'", "data:", "data:");
        return $"<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src {style}; font-src {font}; img-src {img}; script-src {scripts}; object-src 'none'; frame-src 'none'; form-action 'none'\">";
    }

    /// <summary>The shell's own page: its policy is the most a message frame may get.</summary>
    public static string Shell(string shellJs, string background) =>
        $"<!doctype html><html style=\"background:{background}\"><head><meta charset=utf-8>" +
        "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline' *; font-src * data:; img-src * data:; script-src 'unsafe-inline'; frame-src 'self' about:; object-src 'none'; form-action 'none'\">" +
        $"<style>html,body{{margin:0;padding:0;height:100%;overflow:hidden}}</style></head><body><script>{shellJs}</script></body></html>";

    /// <summary>A plain page on a solid background (the switching and airspace probes).</summary>
    public static string Solid(string background, string text, bool images = false) =>
        $"<!doctype html><html><head><meta charset=utf-8>{Csp(images, null)}<base target=_blank>" +
        $"<style>html,body{{margin:0;height:100%;overflow:hidden;background:{background};color:#fff;font:14px sans-serif}}</style></head><body><p style=\"margin:16px\">{text}</p></body></html>";

    /// <summary>HTML mail under the dark conversion: hidden until email_dark.js ran (1.5 s failsafe), allowed by its hash.</summary>
    public static string Dark(string body, string darkJs)
    {
        var script = darkJs.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        const string Css = "html{background:#0a0a0a}body{color:#e6e6e6;background:#0a0a0a}a{color:#8ab4f8}" +
            "@keyframes barbican-dark-reveal{to{visibility:visible}}" +
            "html.barbican-dark-pending{background-color:#0a0a0a!important}" +
            "html.barbican-dark-pending body{visibility:hidden;animation:barbican-dark-reveal 0s 1.5s forwards}";
        return $"<!doctype html><html class=\"{DarkPendingClass}\" style=\"color-scheme:light\"><head><meta charset=utf-8>{Csp(false, script)}<script>{script}</script><base target=_blank>" +
            $"<style>body{{margin:16px;font:14px sans-serif}}{Css}</style></head><body>{body}</body></html>";
    }
}
