// Mirrors: Rust main.rs Current { html, text, remote, allow } :112 and render_current_then :142 (which body is shown; tag rust-final);
// Bulwark components/email/email-viewer.tsx (plain-text view "Shown as plain text" › Show formatted, spec I-214). Owner body.

namespace Barbican.Core.Mail.Body;

/// <summary>The body of the open message as it can be shown: its HTML (null for a text-only message) and its text.</summary>
/// <param name="Html">The HTML parts, cid: images already inlined; null when the message has no HTML part to show.</param>
/// <param name="Text">The text parts.</param>
public sealed record MailContent(string? Html, string Text)
{
    /// <summary>The HTML refers to remote content.</summary>
    public bool HasRemoteContent => Html is not null && MailHtml.HasRemoteContent(Html);

    /// <summary>The body markup to put in the page: the HTML, or the text in a pre (Rust render_current_then).</summary>
    /// <param name="plainView">Settings › Accessibility › "Show messages as plain text" (and no per-message "Show formatted").</param>
    public string BodyMarkup(bool plainView)
    {
        if (Html is not null && !plainView)
        {
            return Html;
        }

        if (Html is not null && string.IsNullOrWhiteSpace(Text))
        {
            return "<pre>" + MailHtml.Escape(MailHtml.HtmlToText(Html)) + "</pre>";
        }

        return "<pre>" + MailHtml.Escape(Text) + "</pre>";
    }
}
