// Mirrors: Rust compose.rs (the composer fields), Bulwark components/email/email-composer.tsx (draft state), lib/jmap/client.ts createDraft/updateDraft/sendEmail; RFC 8621 section 4 (Email/set create with $draft), section 7 (EmailSubmission). Contract from the M3 plan section 4.3; owner send-pipeline.

using Barbican.Core.Editor;
using Barbican.Core.Jmap.Submission;

namespace Barbican.Core.Compose;

/// <summary>Everything the composer holds, handed to the pipeline to save or send. The recipient fields are the field text (the source of truth, B-COMP-019).</summary>
public sealed record ComposeDraft
{
    public required string AccountId { get; init; }

    public ComposeKind Kind { get; init; }

    /// <summary>The host-issued composer handle, never supplied by an add-on.</summary>
    public string? ComposerId { get; init; }

    public string? AuthoredText { get; init; }

    public bool IsQuickReply { get; init; }

    /// <summary>Selected keywords for the sent copy.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>The draft on the server this composer replaced or reopened; null before the first save.</summary>
    public string? DraftId { get; init; }

    /// <summary>The message answered or forwarded (for the $answered / $forwarded keyword and the quoted <c>cid:</c> parts).</summary>
    public string? SourceEmailId { get; init; }

    public required string IdentityId { get; init; }

    public FromOverride? FromOverride { get; init; }

    public string To { get; init; } = "";

    public string Cc { get; init; } = "";

    public string Bcc { get; init; } = "";

    public string Subject { get; init; } = "";

    /// <summary>The body as the editor holds it; the pipeline derives the HTML and text parts (DocHtml.Export, DocHtml.ToText).</summary>
    public required Doc Body { get; init; }

    public bool PlainText { get; init; }

    public IReadOnlyList<DraftAttachment> Attachments { get; init; } = [];

    public string? InReplyTo { get; init; }

    public IReadOnlyList<string> References { get; init; } = [];

    public bool ReadReceipt { get; init; }

    public MessagePriority Priority { get; init; }
}
