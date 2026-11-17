// Mirrors: Rust mailimport.rs:45-150 (lines, is_envelope_line, split_mbox, header_fields, mbox_state, received_at, items_of, import_entry), :268 message_id, :381 summary;
// RFC 4155 (the mbox format, mboxrd quoting), RFC 5322 (Date, Message-ID), RFC 8621 section 4.8; Bulwark lib/mail-import/{mbox,engine}.ts (isEnvelopeLine, mboxState).
// Stub from stage F of M2; owner unified.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Mail.Import;

/// <summary>The pure parts of reading mbox and .eml files.</summary>
public static class MboxFormat
{
    /// <summary>"From sender Tue May  5 10:11:12 2020" (a zone before the year, or "remote from host" after it): an envelope line, not a body line.</summary>
    public static bool IsEnvelopeLine(ReadOnlySpan<byte> line)
    {
        throw new PortPendingException("mailimport.rs:66 is_envelope_line");
    }

    /// <summary>Splits an mbox file into messages (mboxo and mboxrd): a separator is an envelope line at the start or after an empty line (that empty line is dropped); >From lines lose one >.</summary>
    public static IReadOnlyList<byte[]> Split(ReadOnlySpan<byte> mbox)
    {
        throw new PortPendingException("mailimport.rs:85 split_mbox");
    }

    /// <summary>The header block of a message, unfolded, by lower-cased name.</summary>
    public static IReadOnlyList<(string Name, string Value)> HeaderFields(ReadOnlySpan<byte> message)
    {
        throw new PortPendingException("mailimport.rs:93 header_fields");
    }

    /// <summary>Keywords from the status headers mail programs write (Status, X-Status, X-Mozilla-Status, the labels header, X-Keywords), and whether the message was deleted; with none of them it is imported as read.</summary>
    public static (IReadOnlyList<string> Keywords, bool Expunged) State(IReadOnlyList<(string Name, string Value)> fields)
    {
        throw new PortPendingException("mailimport.rs:114 mbox_state");
    }

    /// <summary>The arrival time from the Date header (RFC 5322) as RFC 3339 UTC, or null.</summary>
    public static string? ReceivedAt(IReadOnlyList<(string Name, string Value)> fields)
    {
        throw new PortPendingException("mailimport.rs:148 received_at");
    }

    /// <summary>The messages of one file: an .mbox (or anything starting with an envelope line) is split, anything else is one message; deleted messages are left out.</summary>
    public static IReadOnlyList<ImportItem> ItemsOf(string fileName, ReadOnlySpan<byte> content)
    {
        throw new PortPendingException("mailimport.rs:169 items_of");
    }

    /// <summary>The Email/import entry for an uploaded message: blobId, mailboxIds, keywords, receivedAt when known.</summary>
    public static JsonObject ImportEntry(string blobId, string mailboxId, ImportItem item)
    {
        throw new PortPendingException("mailimport.rs:232 import_entry");
    }

    /// <summary>The Message-ID of a raw message without angle brackets, or null.</summary>
    public static string? MessageId(ReadOnlySpan<byte> message)
    {
        throw new PortPendingException("mailimport.rs:268 message_id");
    }

    /// <summary>"Imported 2 messages, 3 already in the folder" and "Imported 3 messages, 1 not imported: reason".</summary>
    public static string Summary(int imported, int already, int failed, string? firstError)
    {
        throw new PortPendingException("mailimport.rs:381 summary");
    }
}
