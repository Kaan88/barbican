// Mirrors: Bulwark lib/phishing-links.ts (isPhishingMarked :13, turnOffLinks :26; used by components/email/email-viewer.tsx:3310/3320 on the sanitised body
// before it goes into the reader frame); RFC 9051 section 2.3.2 ($phishing: clients should warn and disable links). Bulwark 1.21.2 has no other link
// check: there is no link-text-versus-address warning, so none is made here (docs/milestones/M6b-feature-parity.md G-049). Owner reader.

using AngleSharp.Html.Parser;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Safety;

/// <summary>Turns off the links of a message the user reported as phishing.</summary>
public static class PhishingLinks
{
    /// <summary>The keyword a reported message carries (RFC 9051 section 2.3.2).</summary>
    public const string Keyword = "$phishing";

    private const string LinkOffStyle = "color:inherit !important;text-decoration:underline dotted !important;cursor:not-allowed !important";
    private const string NoteStyle = "position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap";
    private static readonly string[] Removed = ["href", "xlink:href", "target", "rel", "ping", "download", "tabindex"];

    /// <summary>Whether the message carries the phishing mark.</summary>
    public static bool IsMarked(Keywords? keywords) => keywords?.IsSet(Keyword) == true;

    /// <summary>
    /// <paramref name="html"/> with every <c>a</c> and <c>area</c> stripped of its address (so it cannot be clicked, opened in a new tab or reached with Tab) and
    /// marked as off; a link also gets a tooltip, a dotted look and a note a screen reader hears after its text. Parses the document: call off the UI thread.
    /// </summary>
    public static string TurnOff(string html, string title, string note)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(note);
        if (html.Length == 0 || !HasLink(html))
        {
            return html;
        }

        var document = new HtmlParser(new HtmlParserOptions { IsScripting = false }).ParseDocument(html);
        var links = document.QuerySelectorAll("a, area");
        if (links.Length == 0)
        {
            return html;
        }

        foreach (var link in links)
        {
            foreach (var name in Removed)
            {
                link.RemoveAttribute(name);
            }

            link.SetAttribute("data-link-off", string.Empty);
            link.SetAttribute("aria-disabled", "true");
            link.SetAttribute("title", title);
            if (!string.Equals(link.LocalName, "a", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var style = link.GetAttribute("style");
            link.SetAttribute("style", (string.IsNullOrEmpty(style) ? string.Empty : style + ";") + LinkOffStyle);
            var span = document.CreateElement("span");
            span.SetAttribute("data-link-off-note", string.Empty);
            span.SetAttribute("style", NoteStyle);
            span.TextContent = " " + note;
            link.AppendChild(span);
        }

        // a full document keeps its head (styles); a fragment stays a fragment
        return html.Contains("<html", StringComparison.OrdinalIgnoreCase) ? document.DocumentElement.OuterHtml : document.Body!.InnerHtml;
    }

    // /<(a|area)\b/i: a tag name that is exactly a or area
    private static bool HasLink(string html)
    {
        for (var at = html.IndexOf('<', StringComparison.Ordinal); at >= 0 && at + 1 < html.Length; at = html.IndexOf('<', at + 1))
        {
            var rest = html.AsSpan(at + 1);
            if ((rest.StartsWith("a", StringComparison.OrdinalIgnoreCase) && (rest.Length == 1 || !char.IsAsciiLetterOrDigit(rest[1]))) ||
                (rest.StartsWith("area", StringComparison.OrdinalIgnoreCase) && (rest.Length == 4 || !char.IsAsciiLetterOrDigit(rest[4]))))
            {
                return true;
            }
        }

        return false;
    }
}
