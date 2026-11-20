// Mirrors Bulwark components/email/email-composer.tsx:4652-4671 (onComposeSend request).
using Barbican.Core.Compose;

namespace Barbican.Core.Plugins.Api.ComposeSend;

/// <summary>Host-prepared composer content for one add-on takeover invocation.</summary>
public sealed record PluginComposeSendRequest(
    IReadOnlyList<string> To, IReadOnlyList<string> Cc, IReadOnlyList<string> Bcc,
    string Subject, string HtmlBody, string TextBody, string IdentityId, string FromEmail, string FromName,
    string? InReplyTo, IReadOnlyList<string>? References, string? DelayedUntil, string? LabelToken,
    IReadOnlyList<DraftAttachment> Attachments);
