// Mirrors: Rust dark_mail.rs (whole); Bulwark lib/email-dark-mode.ts (emailDarkModeCss, resolveColorSchemeMedia). Stub from F2; owner body.

using Barbican.Core.Common;

namespace Barbican.Core.Mail.Body;

/// <summary>Dark mode for HTML mail: the page parts around the recolouring script that runs in the WebView.</summary>
public static class DarkMail
{
    /// <summary>Class on <c>html</c> that keeps the body hidden until the script ran (1.5 s failsafe).</summary>
    public const string PendingClass = "barbican-dark-pending";

    /// <summary>The canvas the converted mail sits on (the reader's dark background).</summary>
    public const string Background = "#0a0a0a";

    /// <summary>The recolouring script (email_dark.js, embedded by the body stream).</summary>
    public static string Script => throw new PortPendingException("DarkMail.Script: body stream");

    /// <summary>Whether the mail ships its own dark styles (<c>prefers-color-scheme: dark</c>).</summary>
    public static bool HasNativeDark(string html) => throw new PortPendingException("DarkMail.HasNativeDark: body stream");

    /// <summary><c>color-scheme</c> of the page: dark only for a mail with its own dark styles shown dark.</summary>
    public static string ColorScheme(bool dark, bool native) => throw new PortPendingException("DarkMail.ColorScheme: body stream");

    /// <summary>The fallback colours and the pending rules.</summary>
    public static string Css() => throw new PortPendingException("DarkMail.Css: body stream");

    /// <summary>Rewrites <c>(prefers-color-scheme: dark|light)</c> inside style blocks so the matching one always applies and the other never does.</summary>
    public static string ResolveColorSchemeMedia(string html, bool dark) => throw new PortPendingException("DarkMail.ResolveColorSchemeMedia: body stream");
}
