// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Barbican.Core.Mail.Model;

/// <summary>A mailbox (RFC 8621 §2) with its counts.</summary>
public sealed record Mailbox
{
    public required string Id { get; init; }

    public string Name { get; init; } = "";

    public string? ParentId { get; init; }

    public MailboxRole Role { get; init; }

    public int SortOrder { get; init; }

    public int TotalEmails { get; init; }

    public int UnreadEmails { get; init; }

    public int TotalThreads { get; init; }

    public int UnreadThreads { get; init; }

    public bool IsSubscribed { get; init; } = true;

    /// <summary>Properties the model does not name, kept so nothing is lost on a round trip.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}
