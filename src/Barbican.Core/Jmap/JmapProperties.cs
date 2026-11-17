// Mirrors: Rust jmap.rs:1637 MAILBOX_PROPERTIES, :1640 EMAIL_LIST_PROPERTIES, :1643 EMAIL_FULL_PROPERTIES, :1647 BODY_PROPERTIES,
// :1357 list_sort; RFC 8621 §2 (Mailbox), §4.1 (Email), §4.1.4 (EmailBodyPart). Owner jmap.

using System.Text.Json.Nodes;

namespace Barbican.Core.Jmap;

/// <summary>The property lists the client asks for.</summary>
public static class JmapProperties
{
    /// <summary>A folder in the tree.</summary>
    public static readonly IReadOnlyList<string> Mailbox =
        ["id", "name", "parentId", "role", "sortOrder", "totalEmails", "unreadEmails", "totalThreads", "unreadThreads", "myRights", "isSubscribed"];

    /// <summary>A list row.</summary>
    public static readonly IReadOnlyList<string> EmailList =
    [
        "id", "threadId", "mailboxIds", "keywords", "size", "receivedAt", "sentAt", "attachments", "from", "to", "cc", "subject", "preview",
        "hasAttachment", "blobId", "header:Authentication-Results:all",
    ];

    /// <summary>A message in the reader.</summary>
    public static readonly IReadOnlyList<string> EmailFull =
    [
        "id", "threadId", "mailboxIds", "keywords", "size", "receivedAt", "sentAt", "from", "to", "cc", "bcc", "replyTo", "sender", "subject",
        "messageId", "inReplyTo", "references", "textBody", "htmlBody", "attachments", "bodyValues", "hasAttachment", "preview", "headers", "blobId",
    ];

    /// <summary>A body part.</summary>
    public static readonly IReadOnlyList<string> Body = ["partId", "blobId", "size", "name", "type", "charset", "cid", "disposition", "language"];

    /// <summary>The largest body value fetched for the reader (Rust jmap.rs:657).</summary>
    public const long MaxBodyValueBytes = 2_000_000;

    /// <summary>A property list as a JSON array.</summary>
    public static JsonArray Json(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return [.. names.Select(n => (JsonNode)n)];
    }

    /// <summary>Newest first (RFC 8621 §4.4.2 receivedAt).</summary>
    public static JsonArray ListSort() => [new JsonObject { ["property"] = "receivedAt", ["isAscending"] = false }];
}
