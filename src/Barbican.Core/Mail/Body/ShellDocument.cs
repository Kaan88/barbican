// Mirrors: Rust mailview.rs:127 shell_html (tag rust-final), plus base-uri 'none' as in Bulwark
//   lib/email-sanitization.ts:116. Bulwark has no persistent shell (each message is its own srcdoc iframe).
// Fable review 1 F-03: the shell's script-src is the SHA-256 of its one inline script, never 'unsafe-inline' (a srcdoc frame inherits
//   its embedder's policy, so this is a second lock on top of each frame's script-src 'none'); the script carries the dark pass
//   (email_dark.js wrapped as window.__bbDarkPass(document)) ahead of shell.js, which calls it on the frame document.
using System.Text.RegularExpressions;

namespace Barbican.Core.Mail.Body;

/// <summary>
/// The body shell's own page: one persistent document that never navigates and hosts every message
/// as a sandboxed srcdoc frame (src/Barbican.App/Assets/mail/shell.js). Its policy is the most any
/// message frame may get; each frame narrows it with <see cref="MailCsp"/>.
/// </summary>
public static partial class ShellDocument
{
    /// <summary>The shell's policy without its script source (<see cref="Policy"/> adds the hash).</summary>
    public const string BasePolicy =
        "default-src 'none'; style-src 'unsafe-inline' *; font-src * data:; img-src * data:; frame-src 'self' about:; object-src 'none'; form-action 'none'; base-uri 'none'";

    /// <summary>The dark pass as a function of the document it converts (email_dark.js reads only <c>document</c> as a global).</summary>
    public static string DarkPass => "window.__bbDarkPass=function(document){\n" + DarkMailScript.Source + "\n};";

    /// <summary>The policy for a shell whose one inline script is <paramref name="script"/>.</summary>
    public static string Policy(string script) => BasePolicy.Replace(
        "frame-src", $"script-src {MailCsp.ScriptSource(script)}; frame-src", StringComparison.Ordinal);

    /// <summary>The one inline script of the shell: the dark pass, then <paramref name="shellScript"/>, with LF line ends (the HTML parser
    /// turns CRLF into LF before the hash is checked).</summary>
    public static string Script(string shellScript)
    {
        ArgumentNullException.ThrowIfNull(shellScript);
        return (DarkPass + "\n" + shellScript).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

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

        var script = Script(shellScript);
        return $"<!doctype html><html style=\"background:{background}\"><head><meta charset=utf-8>" +
            $"<meta http-equiv=\"Content-Security-Policy\" content=\"{Policy(script)}\">" +
            MailCsp.NoPrefetchTag +
            "<style>html,body{margin:0;padding:0;height:100%;overflow:hidden}</style></head>" +
            $"<body><script>{script}</script></body></html>";
    }

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HexColor();
}
