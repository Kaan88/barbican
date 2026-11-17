// Mirrors: Rust main.rs (tag rust-final) body_text :664, html_to_text :578, has_remote_images :129, esc :194; Bulwark lib/email-sanitization.ts
// (meta refresh and <base> never survive DOMPurify there) and lib/external-mail.ts (what counts as external content). RFC 8621 §4.1.4
// (textBody / htmlBody parts, bodyValues by partId). Stalwart crates/jmap/src/email/get.rs (bodyValues, isTruncated). Owner body.

using System.Text;
using System.Text.RegularExpressions;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Body;

/// <summary>The text of a message's body parts and the small HTML helpers around showing it.</summary>
public static partial class MailHtml
{
    private static readonly string[] BreakTags = ["<br", "</p", "</div", "</tr", "<li", "</h"];

    private static readonly (string Open, string Close)[] SkippedBlocks = [("<style", "</style>"), ("<script", "</script>"), ("<head", "</head>")];

    private static readonly string[] RemoteMarkers = ["src=http", "url(http", "background=http", "srcset=http", "src=//", "url(//", "poster=http"];

    /// <summary>The HTML and the plain text of a message: its htmlBody and textBody part values, each joined by a newline (Rust body_text).</summary>
    public static (string Html, string Text) BodyText(EmailBody message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return (Collect(message, message.HtmlBody), Collect(message, message.TextBody));
    }

    /// <summary>The message has an HTML part to show (Rust: an htmlBody part of type text/html).</summary>
    public static bool IsHtml(EmailBody message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.HtmlBody.Any(p => string.Equals(p.Type, "text/html", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Escapes text for an HTML body (Rust esc: &amp;, &lt;, &gt;).</summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
    }

    /// <summary>Whether the HTML loads anything from the network (images, backgrounds, srcsets; Rust has_remote_images plus poster).</summary>
    public static bool HasRemoteContent(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var l = html.ToLowerInvariant().Replace("\"", string.Empty, StringComparison.Ordinal).Replace("'", string.Empty, StringComparison.Ordinal);
        return RemoteMarkers.Any(m => l.Contains(m, StringComparison.Ordinal));
    }

    /// <summary>
    /// Removes what could move the message frame itself somewhere else: <c>&lt;meta http-equiv=refresh&gt;</c> (a timed navigation the
    /// sandbox does not stop, and a network request on Linux where no resource filter exists). <c>&lt;base&gt;</c> is left to the CSP
    /// (<c>base-uri 'none'</c>).
    /// </summary>
    public static string StripRefresh(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return html.Contains("refresh", StringComparison.OrdinalIgnoreCase) ? MetaRefresh().Replace(html, string.Empty) : html;
    }

    /// <summary>Plain text of an HTML body: tags dropped, style/script/head skipped, breaks for block ends, at most one blank line (Rust html_to_text).</summary>
    public static string HtmlToText(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var output = new StringBuilder(html.Length);
        var i = 0;
        while (i < html.Length)
        {
            if (html[i] == '<')
            {
                i = SkipBlock(html, i);
                if (BreakTags.Any(t => string.Compare(html, i, t, 0, t.Length, StringComparison.OrdinalIgnoreCase) == 0))
                {
                    output.Append('\n');
                }

                var e = html.IndexOf('>', i);
                if (e < 0)
                {
                    break;
                }

                i = e + 1;
            }
            else
            {
                var next = html.IndexOf('<', i);
                next = next < 0 ? html.Length : next;
                output.Append(html, i, next - i);
                i = next;
            }
        }

        var text = output.ToString()
            .Replace("&nbsp;", " ", StringComparison.Ordinal).Replace("&amp;", "&", StringComparison.Ordinal).Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal).Replace("&quot;", "\"", StringComparison.Ordinal).Replace("&#39;", "'", StringComparison.Ordinal);
        var result = new StringBuilder(text.Length);
        var blank = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                blank++;
                if (blank > 1)
                {
                    continue;
                }
            }
            else
            {
                blank = 0;
            }

            result.Append(line).Append('\n');
        }

        return result.ToString().Trim();
    }

    private static int SkipBlock(string html, int i)
    {
        foreach (var (open, close) in SkippedBlocks)
        {
            if (string.Compare(html, i, open, 0, open.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                var e = html.IndexOf(close, i, StringComparison.OrdinalIgnoreCase);
                if (e >= 0)
                {
                    i = e;
                }
            }
        }

        return i;
    }

    private static string Collect(EmailBody message, IReadOnlyList<BodyPart> parts) =>
        string.Join('\n', parts.Select(p => p.PartId is { } id && message.BodyValues.TryGetValue(id, out var v) ? v.Value : null).OfType<string>());

    [GeneratedRegex("<meta\\b[^>]*http-equiv\\s*=\\s*[\"']?\\s*refresh[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex MetaRefresh();
}
