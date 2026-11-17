// Mirrors: M2 plan section 4.5 (IAttachedMessageOpener); RFC 8621 section 4.9 (Email/parse); Rust reader.rs:728 is_message_attachment; Stalwart crates/jmap/src/email/parse.rs.
// Contract from stage F of M2; owner unified (the reader consumes it).

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Parse;

/// <summary>Opens an attached message (message/rfc822 or .eml) as a message.</summary>
public interface IAttachedMessageOpener
{
    /// <summary>False when the account does not offer Email/parse (the reader then offers only Save).</summary>
    bool IsAvailable { get; }

    Task<Result<EmailBody>> OpenAsync(MailTarget container, AttachmentRef attachment, CancellationToken ct);
}
