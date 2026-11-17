// Mirrors: Rust mailview.rs:127 shell_html (tag rust-final), plus base-uri 'none' as in Bulwark
//   lib/email-sanitization.ts:116. Bulwark has no persistent shell (each message is its own srcdoc iframe).
using System.Text.RegularExpressions;

namespace Barbican.Core.Mail.Body;

/// <summary>
/// The body shell's own page: one persistent document that never navigates and hosts every message
/// as a sandboxed srcdoc frame (src/Barbican.App/Assets/mail/shell.js). Its policy is the most any
/// message frame may get; each frame narrows it with <see cref="MailCsp"/>.
/// </summary>
public static partial class ShellDocument
{
    public const string Policy =
        "default-src 'none'; style-src 'unsafe-inline' *; font-src * data:; img-src * data:; script-src 'unsafe-inline'; frame-src 'self' about:; object-src 'none'; form-action 'none'; base-uri 'none'";

    /// <summary>The shell page with <paramref name="shellScript"/> inline; <paramref name="background"/> is a CSS hex colour.</summary>
    public static string Build(string shellScript, string background)
    {
        ArgumentNullException.ThrowIfNull(shellScript);
        ArgumentNullException.ThrowIfNull(background);
        if (!HexColor().IsMatch(background))
        {
            throw new ArgumentException("background must be a CSS hex colour (#rgb or #rrggbb)", nameof(background));
        }

        if (shellScript.Contains("</script", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("the shell script must not contain a closing script tag", nameof(shellScript));
        }

        return $"<!doctype html><html style=\"background:{background}\"><head><meta charset=utf-8>" +
            $"<meta http-equiv=\"Content-Security-Policy\" content=\"{Policy}\">" +
            "<style>html,body{margin:0;padding:0;height:100%;overflow:hidden}</style></head>" +
            $"<body><script>{shellScript}</script></body></html>";
    }

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HexColor();
}
