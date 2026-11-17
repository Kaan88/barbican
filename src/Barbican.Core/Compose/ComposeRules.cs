// Mirrors: Rust compose.rs:48 is_blank, :191 delivery_addresses, :729 find_placeholders; Bulwark lib/placeholder-check.ts findUnfilledPlaceholders, lib/email-composer-utils.ts deliveryAddresses; Bulwark components/email/email-composer.tsx. Contract stub from the M3 plan section 4.3; owner send-pipeline replaces the bodies.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Compose;

/// <summary>The composer's pure rules (stub until stream send-pipeline lands: members throw <see cref="PortPendingException"/>).</summary>
public static class ComposeRules
{
    /// <summary>Nothing worth keeping: no recipients, subject or attachments, and a body that is empty apart from the signature Barbican put there (and dashes).</summary>
    public static bool IsBlank(IReadOnlyList<string> fields, string body, IReadOnlyList<string> signatures, int attachments) => throw new PortPendingException("ComposeRules.IsBlank");

    /// <summary>Addresses in Delivered-To, X-Original-To, X-Delivered-To, X-Original-Delivered-To in that header priority, the address inside <c>&lt;&gt;</c> or the first token with an @, de-duplicated case-insensitively.</summary>
    public static IReadOnlyList<string> DeliveryAddresses(IReadOnlyList<EmailHeader> headers) => throw new PortPendingException("ComposeRules.DeliveryAddresses");

    /// <summary>Unfilled placeholders: template fields ({{ name }}, also in the subject), bracketed fill-ins ([TIME], [Your Name], [insert date here]), TODO-style markers, circle stand-ins; links and ordinary brackets are text.</summary>
    public static IReadOnlyList<string> FindPlaceholders(string subject, string body) => throw new PortPendingException("ComposeRules.FindPlaceholders");
}
