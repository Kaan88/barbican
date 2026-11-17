// Mirrors: M2 plan section 4.5 (ReaderBanner); Rust reader.rs:412 banners (unsub, receipt, invite, trust); Bulwark components/email/{unsubscribe-banner,read-receipt-banner,calendar-invitation-banner}.tsx.
// Contract from stage F of M2; owner reader.

namespace Barbican.Presentation.Reader;

/// <summary>A banner above the message body.</summary>
/// <param name="Id">A stable id ("unsubscribe", "receipt", "invitation", "remote-content").</param>
/// <param name="Title">The first line.</param>
/// <param name="Detail">The second line, or null.</param>
/// <param name="IsWarning">True for a warning tone, false for a caution or info.</param>
/// <param name="Actions">The buttons: (action id, label).</param>
public sealed record ReaderBanner(string Id, string Title, string? Detail, bool IsWarning, IReadOnlyList<(string ActionId, string Label)> Actions)
{
    /// <summary>A small line under the buttons (an invitation: "Replies arrive with Calendar (M4)").</summary>
    public string? Note { get; init; }
}
