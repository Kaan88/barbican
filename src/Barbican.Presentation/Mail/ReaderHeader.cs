// Mirrors: Rust msgload.rs prefill :175 (r_from, r_email, r_initials, r_color, r_subject, r_date, r_flagged, r_to). Owner mailcore.

namespace Barbican.Presentation.Mail;

/// <summary>The header of the open message.</summary>
public sealed record ReaderHeader(
    string From,
    string Email,
    string Initials,
    int TintIndex,
    string Subject,
    string Date,
    bool Flagged,
    string To)
{
    /// <summary>The tags the message carries, under the subject, each with a remove button (Bulwark email-viewer.tsx:5422; M2 hotfix 2).</summary>
    public IReadOnlyList<RowTag> Tags { get; init; } = [];

    /// <summary>The header of nothing (an empty reader).</summary>
    public static ReaderHeader None { get; } = new(string.Empty, string.Empty, string.Empty, 0, string.Empty, string.Empty, false, string.Empty);
}
