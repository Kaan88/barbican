// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Barbican.Core.Mail.Model;

/// <summary>A message with its parts and body values (EMAIL_FULL_PROPERTIES in Rust jmap.rs:1643).</summary>
public sealed record EmailBody
{
    public required string Id { get; init; }

    public string? ThreadId { get; init; }

    public IReadOnlyDictionary<string, bool> MailboxIds { get; init; } = new Dictionary<string, bool>(StringComparer.Ordinal);

    public Keywords Keywords { get; init; } = Keywords.None;

    public string? Subject { get; init; }

    public string? Preview { get; init; }

    public IReadOnlyList<EmailHeader> Headers { get; init; } = [];

    public IReadOnlyList<EmailAddress> From { get; init; } = [];

    public IReadOnlyList<EmailAddress> To { get; init; } = [];

    public IReadOnlyList<EmailAddress> Cc { get; init; } = [];

    public IReadOnlyList<EmailAddress> Bcc { get; init; } = [];

    public IReadOnlyList<EmailAddress> ReplyTo { get; init; } = [];

    public IReadOnlyList<BodyPart> TextBody { get; init; } = [];

    public IReadOnlyList<BodyPart> HtmlBody { get; init; } = [];

    public IReadOnlyList<Attachment> Attachments { get; init; } = [];

    public IReadOnlyDictionary<string, BodyValue> BodyValues { get; init; } = new Dictionary<string, BodyValue>(StringComparer.Ordinal);

    /// <summary>Inline (<c>cid:</c>) images already resolved to data URIs, by content id (Rust <c>__cidBodyValues</c>).</summary>
    public IReadOnlyDictionary<string, string> CidBodyValues { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>True when the server cut a body value short (J-053, surfaced in the reader).</summary>
    public bool IsTruncated { get; init; }

    public DateTimeOffset? ReceivedAt { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; init; }
}
