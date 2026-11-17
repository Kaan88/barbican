// Mirrors: Rust dark_mail.rs (whole, tag rust-final); Bulwark lib/email-dark-mode.ts (emailDarkModeCss, resolveColorSchemeMedia;
// applyEmailDarkMode is email_dark.js). Stalwart: none (client-side only). Owner body.
//
// The colour rewrite itself runs in the WebView (email_dark.js, allowed by its CSP hash); this class holds the page parts around it:
// the fallback colours, the pending class that hides the light original until the pass ran, and the pinning of the mail's own
// prefers-color-scheme queries to the theme the app shows it in (inside the WebView they would follow the OS).
// Matching is ASCII case-insensitive on the original string (Rust's to_ascii_lowercase keeps byte positions; so does this).

using System.Text;

namespace Barbican.Core.Mail.Body;

/// <summary>Dark mode for HTML mail: the page parts around the recolouring script that runs in the WebView.</summary>
public static class DarkMail
{
    /// <summary>Class on <c>html</c> that keeps the body hidden until the script ran (1.5 s failsafe).</summary>
    public const string PendingClass = "barbican-dark-pending";

    /// <summary>The canvas the converted mail sits on (the reader's dark background).</summary>
    public const string Background = "#0a0a0a";

    private const string Text = "#e6e6e6";
    private const string Link = "#8ab4f8";
    private const string Always = "(min-width: 0px)";
    private const string Never = "(max-width: 0.001px)";
    private const string Query = "prefers-color-scheme";

    /// <summary>The recolouring script (email_dark.js, carried verbatim with LF line ends).</summary>
    public static string Script => DarkMailScript.Source;

    /// <summary>Whether the mail ships its own dark styles (<c>prefers-color-scheme: dark</c>).</summary>
    public static bool HasNativeDark(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var i = 0;
        while (true)
        {
            var p = html.IndexOf(Query, i, StringComparison.OrdinalIgnoreCase);
            if (p < 0)
            {
                return false;
            }

            var j = SkipSpace(html, p + Query.Length);
            if (j < html.Length && html[j] == ':' && StartsAt(html, SkipSpace(html, j + 1), "dark"))
            {
                return true;
            }

            i = p + 1;
        }
    }

    /// <summary><c>color-scheme</c> of the page: dark only for a mail with its own dark styles shown dark.</summary>
    public static string ColorScheme(bool dark, bool native) => dark && native ? "dark" : "light";

    /// <summary>The fallback colours and the pending rules (Bulwark emailDarkModeCss).</summary>
    public static string Css() =>
        $"html{{background:{Background}}}body{{color:{Text};background:{Background}}}a{{color:{Link}}}" +
        "@keyframes barbican-dark-reveal{to{visibility:visible}}" +
        $"html.{PendingClass}{{background-color:{Background}!important}}" +
        $"html.{PendingClass} body{{visibility:hidden;animation:barbican-dark-reveal 0s 1.5s forwards}}";

    /// <summary>
    /// Rewrites <c>(prefers-color-scheme: dark|light)</c> inside style blocks so the matching one always applies and the other never
    /// does (Bulwark resolveColorSchemeMedia). Only <c>&lt;style&gt;</c> blocks are touched.
    /// </summary>
    public static string ResolveColorSchemeMedia(string html, bool dark)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (html.IndexOf(Query, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return html;
        }

        var output = new StringBuilder(html.Length);
        var i = 0;
        while (true)
        {
            var start = html.IndexOf("<style", i, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                break;
            }

            // "<style" must be the whole tag name (not <styles>)
            var after = start + 6 < html.Length ? html[start + 6] : '>';
            if (!(after == '>' || after == '/' || IsAsciiSpace(after)))
            {
                output.Append(html, i, start + 6 - i);
                i = start + 6;
                continue;
            }

            var openEnd = html.IndexOf('>', start);
            if (openEnd < 0)
            {
                break;
            }

            openEnd++;
            var close = html.IndexOf("</style", openEnd, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
            {
                break;
            }

            output.Append(html, i, openEnd - i);
            output.Append(PinQueries(html[openEnd..close], dark));
            i = close;
        }

        output.Append(html, i, html.Length - i);
        return output.ToString();
    }

    // (prefers-color-scheme: x) with any spacing, replaced in one CSS text.
    private static string PinQueries(string css, bool dark)
    {
        var output = new StringBuilder(css.Length);
        var i = 0;
        var last = 0;
        while (i < css.Length)
        {
            if (css[i] == '(' && TryQuery(css, i, out var isDark, out var end))
            {
                output.Append(css, last, i - last);
                output.Append(isDark == dark ? Always : Never);
                i = end;
                last = i;
                continue;
            }

            i++;
        }

        output.Append(css, last, css.Length - last);
        return output.ToString();
    }

    private static bool TryQuery(string css, int open, out bool isDark, out int end)
    {
        isDark = false;
        end = 0;
        var j = SkipSpace(css, open + 1);
        if (!StartsAt(css, j, Query))
        {
            return false;
        }

        j = SkipSpace(css, j + Query.Length);
        if (j >= css.Length || css[j] != ':')
        {
            return false;
        }

        j = SkipSpace(css, j + 1);
        if (StartsAt(css, j, "dark"))
        {
            isDark = true;
            j += 4;
        }
        else if (StartsAt(css, j, "light"))
        {
            j += 5;
        }
        else
        {
            return false;
        }

        j = SkipSpace(css, j);
        if (j >= css.Length || css[j] != ')')
        {
            return false;
        }

        end = j + 1;
        return true;
    }

    private static bool StartsAt(string s, int at, string word) =>
        at <= s.Length - word.Length && string.Compare(s, at, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0;

    private static int SkipSpace(string s, int at)
    {
        while (at < s.Length && IsAsciiSpace(s[at]))
        {
            at++;
        }

        return at;
    }

    // Rust u8::is_ascii_whitespace: space, \t, \n, \x0C, \r
    private static bool IsAsciiSpace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';
}
