// Mirrors: lib/email-sanitization.ts:197-313 (SIGNATURE_SANITIZE_CONFIG, PLUGIN_BODY_SANITIZE_CONFIG, restrictPluginImages, sanitizePluginBodyHtml),
// lib/plugin-sandbox/editor-html.ts:1-8 (textToEditorHtml). Bulwark runs DOMPurify; this is an allow-list walk with the same tag, attribute and URI rules.
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Barbican.Core.Plugins.Api.Composer;

/// <summary>HTML a plugin hands the composer: formatting, links, images and tables only. No script, no event handler, no remote image.</summary>
public static partial class PluginBodyHtml
{
    private static readonly HashSet<string> Tags = new(StringComparer.Ordinal)
    {
        "p", "br", "b", "strong", "i", "em", "u", "a", "span", "div", "img", "table", "thead", "tbody", "tfoot", "tr", "td", "th",
        "ul", "ol", "li", "blockquote", "h1", "h2", "h3", "pre", "code", "s", "hr",
    };

    private static readonly HashSet<string> Attributes = new(StringComparer.Ordinal)
    {
        "href", "style", "class", "src", "alt", "width", "height", "title", "cellpadding", "cellspacing", "border", "valign", "align",
        "bgcolor", "colspan", "rowspan",
    };

    // DOMPurify FORBID_TAGS plus its default FORBID_CONTENTS: these go with everything inside them; any other unknown tag keeps its children.
    private static readonly HashSet<string> WithContents = new(StringComparer.Ordinal)
    {
        "script", "iframe", "object", "embed", "video", "audio", "style", "noscript", "noembed", "noframes", "template", "title", "textarea",
        "xmp", "plaintext", "head", "svg", "math", "annotation-xml", "desc", "foreignobject", "mi", "mn", "mo", "ms", "mtext", "colgroup",
    };

    private static readonly HashSet<string> UriSafe = new(StringComparer.Ordinal) { "alt", "class", "title", "style" };

    /// <summary>Sanitised HTML (empty for blank input).</summary>
    public static string Sanitize(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        if (new HtmlParser().ParseDocument(html).Body is not { } body) return string.Empty;
        Clean(body);
        return body.InnerHtml;
    }

    /// <summary>Plain text as the editor's paragraphs, one per line.</summary>
    public static string FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var output = new StringBuilder();
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
            output.Append("<p>").Append(line.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)).Append("</p>");
        return output.ToString();
    }

    private static void Clean(INode parent)
    {
        foreach (var child in parent.ChildNodes.ToArray())
        {
            if (child is not IElement element)
            {
                if (child.NodeType is NodeType.Comment or NodeType.ProcessingInstruction) parent.RemoveChild(child);
                continue;
            }
            var name = element.LocalName.ToLowerInvariant();
            if (!Tags.Contains(name))
            {
                if (!WithContents.Contains(name))
                {
                    Clean(element);
                    while (element.FirstChild is { } inner) parent.InsertBefore(inner, element);
                }
                parent.RemoveChild(element);
                continue;
            }
            CleanAttributes(element, name);
            if (string.Equals(name, "img", StringComparison.Ordinal) && !EmbeddedImage().IsMatch(element.GetAttribute("src") ?? string.Empty))
            {
                parent.RemoveChild(element);
                continue;
            }
            Clean(element);
        }
    }

    private static void CleanAttributes(IElement element, string tag)
    {
        foreach (var attribute in element.Attributes.ToArray())
        {
            var name = attribute.Name.ToLowerInvariant();
            var keep = Attributes.Contains(name) || name.StartsWith("aria-", StringComparison.Ordinal);
            if (keep && string.Equals(name, "style", StringComparison.Ordinal)) keep = !BadStyle(attribute.Value);
            else if (keep && !UriSafe.Contains(name) && !IsAllowedUri(attribute.Value, dataImage: string.Equals(name, "src", StringComparison.Ordinal) && string.Equals(tag, "img", StringComparison.Ordinal))) keep = false;
            if (!keep) element.RemoveAttribute(attribute.NamespaceUri, attribute.LocalName);
        }
    }

    // DOMPurify drops whitespace and control characters before it tests the scheme, and lets an img src be a data: URI (restricted again to rasters below).
    private static bool IsAllowedUri(string value, bool dataImage)
    {
        var squeezed = AttributeWhitespace().Replace(value, string.Empty);
        return UriPattern().IsMatch(squeezed) || (dataImage && squeezed.StartsWith("data:", StringComparison.OrdinalIgnoreCase));
    }

    private static bool BadStyle(string value)
    {
        var squeezed = Squeeze().Replace(value, string.Empty);
        return squeezed.Contains("expression(", StringComparison.OrdinalIgnoreCase) || squeezed.Contains("javascript:", StringComparison.OrdinalIgnoreCase) ||
            squeezed.Contains("vbscript:", StringComparison.OrdinalIgnoreCase) || squeezed.Contains("-moz-binding", StringComparison.OrdinalIgnoreCase) ||
            squeezed.Contains("@import", StringComparison.OrdinalIgnoreCase);
    }

    // DOMPurify's default ALLOWED_URI_REGEXP, applied to every attribute that is not URI-safe.
    [GeneratedRegex(@"^(?:(?:(?:f|ht)tps?|mailto|tel|callto|sms|cid|xmpp|matrix):|[^a-z]|[a-z+.\-]+(?:[^a-z+.\-:]|$))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex UriPattern();

    [GeneratedRegex(@"^(?:cid:|data:image/(?:png|jpe?g|gif|webp);base64,)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex EmbeddedImage();

    [GeneratedRegex(@"[\u0000-\u0020\u00A0\u1680\u180E\u2000-\u2029\u205F\u3000]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex AttributeWhitespace();

    [GeneratedRegex(@"[\s\p{C}]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex Squeeze();
}
