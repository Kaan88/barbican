// Mirrors: Rust mdn.rs:10 MdnOptions; RFC 8098 (message disposition notifications); Bulwark lib/mdn.ts:43-121. Stub from stage F of M2; owner reader.

namespace Barbican.Core.Mail.Mdn;

/// <summary>What a read receipt is built from.</summary>
public sealed record MdnOptions
{
    /// <summary>The address that asked for the receipt (Disposition-Notification-To): the recipient of the receipt.</summary>
    public string To { get; init; } = string.Empty;

    /// <summary>The identity it is sent from.</summary>
    public string FromEmail { get; init; } = string.Empty;

    public string FromName { get; init; } = string.Empty;

    /// <summary>The original Message-ID, with or without angle brackets.</summary>
    public string OriginalMessageId { get; init; } = string.Empty;

    public string OriginalSubject { get; init; } = string.Empty;

    public string OriginalRecipient { get; init; } = string.Empty;

    /// <summary>True when sent without asking (automatic-action), false for manual-action.</summary>
    public bool Automatic { get; init; }
}
