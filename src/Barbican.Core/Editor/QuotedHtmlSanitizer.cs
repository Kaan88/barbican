// Mirrors: nothing in Rust (its quote cleaner only dropped wrappers); Bulwark lib/email-sanitize (DOMPurify on the reader side). No Stalwart counterpart.
// Decisions: spec/decisions.md D-M3C-01 (quoted original HTML is sanitised on the way into and out of a composer).

using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Barbican.Core.Editor;

/// <summary>
/// Makes the original message's HTML safe to put into a message we send: no event handlers, frames, plugins, forms, base/meta/link, and no
/// scripting or HTML data URLs in link-like attributes. Visible content and inline styles stay. Input that has no risky marker is returned
/// untouched, so clean quotes keep their exact bytes.
/// </summary>
internal static partial class QuotedHtmlSanitizer
{
    private static readonly HashSet<string> Dropped = new(StringComparer.Ordinal)
    {
        "script", "style", "iframe", "frame", "frameset", "object", "embed", "applet", "base", "meta", "link", "noscript", "template",
    };

    private static readonly HashSet<string> Unwrapped = new(StringComparer.Ordinal) { "form" };

    private static readonly HashSet<string> UrlAttributes = new(StringComparer.Ordinal)
    {
        "href", "src", "action", "formaction", "xlink:href", "background", "poster", "data", "srcdoc", "codebase", "cite", "longdesc", "lowsrc", "dynsrc", "ping",
    };

    public static string Sanitize(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (!LooksRisky(html))
        {
            return html;
        }

        var dom = new HtmlParser().ParseDocument(html);
        if (dom.Body is not { } body)
        {
            return "";
        }

        var (remove, unwrap) = Collect(body);
        foreach (var e in remove)
        {
            e.Remove();
        }

        foreach (var e in unwrap)
        {
            var parent = e.ParentElement;
            if (parent is null)
            {
                continue;
            }

            while (e.FirstChild is { } c)
            {
                parent.InsertBefore(c, e);
            }

            e.Remove();
        }

        return body.InnerHtml.Trim();
    }

    private static (List<IElement> Remove, List<IElement> Unwrap) Collect(IElement body)
    {
        var work = new Stack<IElement>();
        work.Push(body);
        var remove = new List<IElement>();
        var unwrap = new List<IElement>();
        while (work.Count > 0)
        {
            foreach (var child in work.Pop().Children)
            {
                if (Dropped.Contains(child.LocalName))
                {
                    remove.Add(child);
                    continue;
                }

                if (Unwrapped.Contains(child.LocalName))
                {
                    unwrap.Add(child);
                }

                CleanAttributes(child);
                work.Push(child);
            }
        }

        return (remove, unwrap);
    }

    private static void CleanAttributes(IElement e)
    {
        foreach (var a in e.Attributes.ToList())
        {
            var n = a.Name.ToLowerInvariant();
            if (n.StartsWith("on", StringComparison.Ordinal) || (UrlAttributes.Contains(n) && IsBadUrl(a.Value)) || (string.Equals(n, "style", StringComparison.Ordinal) && IsBadStyle(a.Value)) || string.Equals(n, "srcdoc", StringComparison.Ordinal))
            {
                e.RemoveAttribute(a.NamespaceUri, a.LocalName);
                e.RemoveAttribute(a.Name);
            }
        }
    }

    private static bool IsBadUrl(string value)
    {
        var sb = new StringBuilder(24);
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                continue;
            }

            sb.Append(char.ToLowerInvariant(c));
            if (sb.Length >= 15)
            {
                break;
            }
        }

        var s = sb.ToString();
        return s.StartsWith("javascript:", StringComparison.Ordinal) || s.StartsWith("vbscript:", StringComparison.Ordinal) || s.StartsWith("data:text/html", StringComparison.Ordinal);
    }

    private static bool IsBadStyle(string value)
    {
        var s = Whitespace().Replace(value, "").ToLowerInvariant();
        return s.Contains("expression(", StringComparison.Ordinal) || s.Contains("javascript:", StringComparison.Ordinal) || s.Contains("vbscript:", StringComparison.Ordinal) || s.Contains("-moz-binding", StringComparison.Ordinal);
    }

    /// <summary>Cheap pre-check: any marker of something the sanitiser removes (whitespace and control characters ignored, so split schemes are caught).</summary>
    private static bool LooksRisky(string html)
    {
        if (Markers().IsMatch(html))
        {
            return true;
        }

        var squeezed = Whitespace().Replace(html, "");
        return squeezed.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
            || squeezed.Contains("vbscript:", StringComparison.OrdinalIgnoreCase)
            || squeezed.Contains("data:text/html", StringComparison.OrdinalIgnoreCase)
            || squeezed.Contains("expression(", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"<\s*/?\s*(script|style|iframe|frame|object|embed|applet|form|base|meta|link|noscript|template)\b|[\s""'/]on[a-z]+\s*=|&#|&colon;|&tab;|&newline;|srcdoc", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 500)]
    private static partial Regex Markers();

    [GeneratedRegex(@"[\s\p{C}]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex Whitespace();
}
