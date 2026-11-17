// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Barbican.Core.Mail.Model;

/// <summary>The row data of a list entry (EMAIL_LIST_PROPERTIES in Rust jmap.rs:1640).</summary>
public sealed record EmailSummary
{
    public required string Id { get; init; }

    public string? ThreadId { get; init; }

    public IReadOnlyDictionary<string, bool> MailboxIds { get; init; } = new Dictionary<string, bool>(StringComparer.Ordinal);

    public Keywords Keywords { get; init; } = Keywords.None;

    public long Size { get; init; }

    public DateTimeOffset? ReceivedAt { get; init; }

    public DateTimeOffset? SentAt { get; init; }

    public IReadOnlyList<EmailAddress> From { get; init; } = [];

    public IReadOnlyList<EmailAddress> To { get; init; } = [];

    public IReadOnlyList<EmailAddress> Cc { get; init; } = [];

    public string? Subject { get; init; }

    public string? Preview { get; init; }

    public bool HasAttachment { get; init; }

    public IReadOnlyList<Attachment> Attachments { get; init; } = [];

    /// <summary>Messages in the conversation (the list's thread count).</summary>
    [JsonIgnore]
    public int ThreadCount { get; init; } = 1;

    /// <summary>The Authentication-Results headers (<c>header:Authentication-Results:all</c>).</summary>
    [JsonPropertyName("header:Authentication-Results:all")]
    public IReadOnlyList<string> AuthenticationResults { get; init; } = [];

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}
