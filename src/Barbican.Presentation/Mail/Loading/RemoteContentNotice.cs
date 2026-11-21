// Mirrors: Bulwark components/email/email-viewer.tsx:6343-6392 (the external-content notice: "External Content", the blocked
// message, "Load images" only in ask mode and "Always trust this sender"); locales/en/common.json email_viewer.external_content_warning,
// load_external_content, trust_sender. Owner reader, I-209.

namespace Barbican.Presentation.Mail.Loading;

/// <summary>The blocked-remote-content notice above a message body and the buttons it offers.</summary>
/// <param name="Label">The small heading above the message ("External Content").</param>
/// <param name="Message">Why the content is missing.</param>
/// <param name="Actions">The buttons, in Bulwark's order: load this message, then trust the sender.</param>
public sealed record RemoteContentNotice(string Label, string Message, IReadOnlyList<(string ActionId, string Label)> Actions)
{
    /// <summary>"Load images" for this message once (ask mode only, Bulwark email-viewer.tsx:6359-6367).</summary>
    public const string Load = "remote-content.load";

    /// <summary>"Always trust this sender", persisted through <c>TrustedSenders</c> (Bulwark email-viewer.tsx:6368-6388).</summary>
    public const string Trust = "remote-content.trust";

    /// <summary>The small heading (Bulwark email-viewer.tsx:6350-6352).</summary>
    public const string Heading = "External Content";

    /// <summary>The message when the user's own setting (not an organisation) holds the content back (Bulwark email_viewer.external_content_warning).</summary>
    public const string BlockedMessage = "Images and external content have been blocked";
}
