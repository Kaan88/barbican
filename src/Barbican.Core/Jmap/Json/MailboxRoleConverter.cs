// Mirrors: RFC 8621 §2 (Mailbox role: the IANA "IMAP Mailbox Name Attributes" registry, lower-case: all, archive, drafts, flagged,
// important, inbox, junk, sent, subscribed, trash); Rust jmap.rs:422 remember_roles. Owner jmap.

using System.Text.Json;
using System.Text.Json.Serialization;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Jmap.Json;

/// <summary>Reads a role string as <see cref="MailboxRole"/> (null or unknown: None) and writes it lower-case.</summary>
public sealed class MailboxRoleConverter : JsonConverter<MailboxRole>
{
    /// <summary>The role of a role string; None for null or a role this client does not know.</summary>
    public static MailboxRole Parse(string? role) => role?.ToLowerInvariant() switch
    {
        "inbox" => MailboxRole.Inbox,
        "drafts" => MailboxRole.Drafts,
        "sent" => MailboxRole.Sent,
        "trash" => MailboxRole.Trash,
        "junk" => MailboxRole.Junk,
        "archive" => MailboxRole.Archive,
        "important" => MailboxRole.Important,
        "all" => MailboxRole.All,
        "flagged" => MailboxRole.Flagged,
        "subscribed" => MailboxRole.Subscribed,
        _ => MailboxRole.None,
    };

    /// <summary>The role string of a role; null for None.</summary>
    public static string? Name(MailboxRole role) => role == MailboxRole.None ? null : role.ToString().ToLowerInvariant();

    /// <inheritdoc/>
    public override MailboxRole Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? Parse(reader.GetString()) : MailboxRole.None;

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, MailboxRole value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (Name(value) is { } name)
        {
            writer.WriteStringValue(name);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
