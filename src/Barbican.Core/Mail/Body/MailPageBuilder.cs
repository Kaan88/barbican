// Mirrors: Rust main.rs:229 page() and :190 html_uncolored (tag rust-final); dark_mail.rs; a11y.rs mail_css / mail_script /
// override_active (ported by mailcore as A11y.MailCss / MailScript / OverrideActive); csp_with :89 (MailCsp, F1).
// Bulwark components/email/email-viewer.tsx (emailAlwaysLightMode, plainTextFont, messageSpacing), lib/email-dark-mode.ts,
// lib/email-sanitization.ts:109 emailIframeCsp. Stalwart: none (client-side only). Owner body.
//
// The frame page is built here, once, for both OSes. Its CSP is the remote-content guard (the only one on Linux, ADR 0005), scripts
// run only by hash (our dark pass and the a11y sizing script), and the frame's sandbox gets allow-scripts only when one of ours is in.

using System.Text;

namespace Barbican.Core.Mail.Body;

/// <summary>Builds the frame document of one message (Rust <c>page</c>).</summary>
public static class MailPageBuilder
{
    private const string Sans = "'Segoe UI',sans-serif";
    private const string Mono = "ui-monospace,Consolas,'Cascadia Mono',monospace";
    private static readonly string[] ColourMarkers = ["color", "background", "<style", "<font", "<svg", "<body"];

    /// <summary>
    /// The page for <paramref name="body"/> (HTML, or text already in a <c>&lt;pre&gt;</c>). In the dark theme (unless
    /// <see cref="MailPageOptions.AlwaysLight"/>): plain text and HTML that sets no colours take the reader's colours; other HTML gets
    /// the dark conversion when <see cref="MailPageOptions.Recolor"/> and the accessibility colour override is off; anything else is on white.
    /// </summary>
    public static MailPage Build(string body, MailPageOptions options)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(options);
        body = MailHtml.StripRefresh(body);
        var a = options.A11y;
        var dark = options.Dark && !options.AlwaysLight;
        var plain = body.StartsWith("<pre>", StringComparison.Ordinal);
        var themed = dark && (plain || Uncoloured(body));
        var recolor = options.Recolor && dark && !themed && !a.OverrideActive();
        var script = (a.MailScript(), recolor) switch
        {
            ({ } s, true) => s + "\n" + DarkMail.Script,
            (null, true) => DarkMail.Script,
            (var s, false) => s,
        };

        // the HTML parser turns CRLF into LF before the CSP hashes a script: hash (and send) it in that form
        script = script?.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var background = dark && a.OverrideActive() ? DarkMail.Background : themed ? options.ReaderBackground : recolor ? DarkMail.Background : "#ffffff";
        var head = new StringBuilder()
            .Append(MailCsp.MetaTag(options.AllowRemote, script))
            .Append("<style>").Append(a.MailCss(dark)).Append("</style>");
        if (script is not null)
        {
            head.Append("<script>").Append(script).Append("</script>");
        }

        var preFont = string.Equals(options.PlainTextFont, "mono", StringComparison.Ordinal) ? Mono : "inherit";
        var margin = options.MessageSpacing switch
        {
            "edge" => 0,
            "always" => 16,
            _ => !plain && FullBleed(body) ? 0 : 16,
        };
        var remote = MailHtml.HasRemoteContent(body);
        if (themed)
        {
            var html = $"<!doctype html><html><head><meta charset=utf-8>{head}<base target=_blank><style>html,body{{background:{options.ReaderBackground};color:#d4d4d4}}" +
                $"body{{margin:{margin}px;font-family:{Sans};font-size:14px;word-wrap:break-word}}pre{{white-space:pre-wrap;font-family:{preFont};margin:0}}" +
                $"a{{color:#f0577a}}img{{max-width:100%;height:auto}}</style></head><body>{body}</body></html>";
            return new MailPage(html, script is not null, background, remote);
        }

        if (recolor)
        {
            // the mail's own dark styles follow the app's theme, not the OS (resolveColorSchemeMedia)
            var scheme = DarkMail.ColorScheme(true, DarkMail.HasNativeDark(body));
            var pinned = DarkMail.ResolveColorSchemeMedia(body, true);
            var html = $"<!doctype html><html class=\"{DarkMail.PendingClass}\" style=\"color-scheme:{scheme}\"><head><meta charset=utf-8>{head}<base target=_blank>" +
                $"<style>body{{margin:{margin}px;font-family:{Sans};font-size:14px;word-wrap:break-word}}pre{{white-space:pre-wrap;font-family:{preFont}}}" +
                $"img{{max-width:100%;height:auto}}{DarkMail.Css()}</style></head><body>{pinned}</body></html>";
            return new MailPage(html, true, background, remote);
        }

        var light = $"<!doctype html><html><head><meta charset=utf-8>{head}<base target=_blank>" +
            $"<style>body{{margin:{margin}px;font-family:{Sans};font-size:14px;color:#111;background:#fff;word-wrap:break-word}}pre{{white-space:pre-wrap;font-family:{preFont}}}" +
            $"img{{max-width:100%;height:auto}}</style></head><body>{body}</body></html>";
        return new MailPage(light, script is not null, background, remote);
    }

    /// <summary>An error page in the reader's colours (Rust msgload open: "Could not load message").</summary>
    public static MailPage Error(string message, MailPageOptions options)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Build("<pre>" + MailHtml.Escape(message) + "</pre>", options with { Recolor = false });
    }

    // HTML without colours, backgrounds or a stylesheet: simple prose (most personal mail).
    private static bool Uncoloured(string html) => !ColourMarkers.Any(k => html.Contains(k, StringComparison.OrdinalIgnoreCase));

    // Bulwark messageSpacing auto: no gutter for a full-bleed HTML layout
    private static bool FullBleed(string body) =>
        body.Contains("<style", StringComparison.OrdinalIgnoreCase)
        && (body.Contains("width=\"100%\"", StringComparison.OrdinalIgnoreCase) || body.Contains("width:100%", StringComparison.OrdinalIgnoreCase)
            || body.Contains("width: 100%", StringComparison.OrdinalIgnoreCase));
}
