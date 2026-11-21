// Mirrors: Rust mailview.rs show(doc, bg, script, on_ready) :158 (tag rust-final): the page is built by the view from this content, so a
// theme change or "Load external content" can build it again without the loader. Owner body.

using Barbican.Core.Mail.Body;
using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>One message for the body view.</summary>
/// <param name="EmailId">The message.</param>
/// <param name="Content">Its HTML (cid: images inlined) and text.</param>
/// <param name="BlockRemote">A body plugin asked that remote content stay blocked whatever the user allows.</param>
/// <param name="OnReady">Runs right before the page is swapped in; false: the page is no longer wanted.</param>
/// <param name="OnShown">Runs after the swap (the content-render hook).</param>
public sealed record BodyRequest(string EmailId, MailContent Content, bool BlockRemote, Func<bool> OnReady, Action? OnShown = null)
{
    /// <summary>The sender is trusted (I-209): remote content loads without asking (never when <see cref="BlockRemote"/>).</summary>
    public bool TrustedSender { get; init; }

    /// <summary>The sender's first address, which "Always trust this sender" would add, or null (Bulwark email-viewer.tsx:6370-6388).</summary>
    public string? SenderAddress { get; init; }

    /// <summary>The body of a fetched message as the reader shows it.</summary>
    public static MailContent ContentOf(EmailBody message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var (html, text) = MailHtml.BodyText(message);
        return new MailContent(MailHtml.IsHtml(message) && html.Length > 0 ? html : null, text);
    }
}
