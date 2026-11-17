// Mirrors: Bulwark lib/email-sanitization.ts (DOMPurify over every HTML body before it reaches the iframe: no scripts, event handlers, forms,
// meta refresh or external frames). Rust had no sanitizer (CSP and sandbox only, main.rs:89, mailview.rs:140); this is new in the port.
// HTML Living Standard §4.2.5 (meta http-equiv), §4.2.3 (base), §4.10.3 (form); Fable review 1 F-03 point 3. Owner body (fix-security
// stream).
//
// The frame's CSP (MailCsp) and sandbox (MailSandbox) are the guards; this is the layer under them that does not depend on the browser
// honouring either: on Linux (WebKitGTK) the CSP is otherwise the only remote-content guard (ADR 0005). It removes every element that can
// run code, load a document, change the base URL, refresh or redirect, prefetch a host, or submit; it unwraps forms (their content stays
// readable) and drops on* handlers, ping, srcdoc, form-action attributes and javascript:/vbscript: URLs. Styles, images, tables and links
// are left exactly as sent. The output is the input unchanged when nothing had to go, so ordinary mail is never reserialised.

using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Body;

/// <summary>Removes active and navigating markup from message HTML.</summary>
public static class MailSanitizer
{
    // removed with their content (svg's own script is a script too: matched by local name in every namespace)
    private static readonly HashSet<string> Removed = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "meta", "link", "base", "iframe", "frame", "frameset", "object", "embed", "applet", "portal", "param", "noembed",
    };

    // replaced by their children
    private static readonly HashSet<string> Unwrapped = new(StringComparer.OrdinalIgnoreCase) { "form" };

    private static readonly HashSet<string> DroppedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ping", "srcdoc", "formaction", "action", "http-equiv",
    };

    private static readonly string[] UrlAttributes = ["href", "src", "xlink:href", "action", "background", "poster", "data"];

    /// <summary>
    /// The HTML without active markup; <paramref name="changed"/> says whether anything was removed (when not, the input is returned as is).
    /// Parses the whole document: call it off the UI thread.
    /// </summary>
    public static string Clean(string html, out bool changed)
    {
        ArgumentNullException.ThrowIfNull(html);
        changed = false;
        if (html.Length == 0)
        {
            return html;
        }

        var document = new HtmlParser(new HtmlParserOptions { IsScripting = false }).ParseDocument(html);
        var removed = 0;
        foreach (var element in document.All.ToList())
        {
            if (Removed.Contains(element.LocalName))
            {
                element.Remove();
                removed++;
            }
            else if (Unwrapped.Contains(element.LocalName))
            {
                Unwrap(element);
                removed++;
            }
            else
            {
                removed += CleanAttributes(element);
            }
        }

        if (removed == 0)
        {
            return html;
        }

        changed = true;
        return Serialize(document, html);
    }

    /// <summary>
    /// The message with every HTML body value cleaned; <see cref="EmailBody.CidBodyValues"/> keeps the first original of a part that changed.
    /// </summary>
    public static EmailBody Apply(EmailBody message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Dictionary<string, BodyValue>? values = null;
        Dictionary<string, string>? originals = null;
        foreach (var part in message.HtmlBody)
        {
            if (part.PartId is not { } pid || !message.BodyValues.TryGetValue(pid, out var value) || !IsHtml(part.Type))
            {
                continue;
            }

            var clean = Clean(value.Value, out var changed);
            if (!changed)
            {
                continue;
            }

            values ??= new Dictionary<string, BodyValue>(message.BodyValues, StringComparer.Ordinal);
            originals ??= new Dictionary<string, string>(message.CidBodyValues, StringComparer.Ordinal);
            originals.TryAdd(pid, value.Value);
            values[pid] = value with { Value = clean };
        }

        return values is null ? message : message with { BodyValues = values, CidBodyValues = originals! };
    }

    private static bool IsHtml(string? type) => type is null || type.Contains("html", StringComparison.OrdinalIgnoreCase);

    private static int CleanAttributes(IElement element)
    {
        var removed = 0;
        foreach (var attribute in element.Attributes.ToList())
        {
            var name = attribute.Name;
            var drop = name.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                || DroppedAttributes.Contains(name)
                || (UrlAttributes.Contains(name, StringComparer.OrdinalIgnoreCase) && IsScriptUrl(attribute.Value));
            if (drop)
            {
                element.RemoveAttribute(attribute.NamespaceUri, attribute.LocalName);
                if (element.HasAttribute(name))
                {
                    element.RemoveAttribute(name);
                }

                removed++;
            }
        }

        return removed;
    }

    // javascript: and vbscript: after the browser's own trimming of controls and whitespace
    private static bool IsScriptUrl(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var compact = new string([.. value.Where(c => c > ' ')]);
        return compact.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || compact.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase);
    }

    private static void Unwrap(IElement element)
    {
        var parent = element.Parent;
        if (parent is null)
        {
            return;
        }

        while (element.FirstChild is { } child)
        {
            parent.InsertBefore(child, element);
        }

        element.Remove();
    }

    // a whole document stays a whole document (its <html>/<body> attributes reach the frame as before); a fragment stays a fragment
    private static string Serialize(IDocument document, string original)
    {
        var whole = original.Contains("<html", StringComparison.OrdinalIgnoreCase) || original.Contains("<body", StringComparison.OrdinalIgnoreCase);
        return whole
            ? document.DocumentElement.OuterHtml
            : (document.Head?.InnerHtml ?? string.Empty) + (document.Body?.InnerHtml ?? string.Empty);
    }
}
