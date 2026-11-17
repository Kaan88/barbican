// Mirrors: Rust chips.rs:240 blur_commits, :332 sources_from_cards, :356 recent_from_sent; Bulwark stores/contact-store.ts, email-composer.tsx recipient field. Contract stub from the M3 plan section 4.5; owner composer-ui replaces the bodies.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Compose.Recipients;

/// <summary>The data side of the recipient chips (stub until stream composer-ui lands).</summary>
public static class ChipSources
{
    /// <summary>Leaving a field: blank text never commits; with no suggestion list open, or when the text holds a comma or semicolon, it commits; otherwise (suggestions open) only when it already is a valid address, so partial text such as "sa" stays in the input.</summary>
    public static bool BlurCommits(string input, bool suggestionsOpen) => throw new PortPendingException("ChipSources.BlurCommits");

    /// <summary>JSContact cards as suggestion sources: a person per card address (preferred address first; the name is <c>name.full</c> or the given and surname components), and a group per <c>kind: group</c> card with the members (by uid with or without <c>urn:uuid:</c>, or id) that have an address and a true flag.</summary>
    public static (IReadOnlyList<(string Name, string Email)> People, IReadOnlyList<(string Id, string Name, IReadOnlyList<Recipient> Members)> Groups) SourcesFromCards(IReadOnlyList<JsonObject> cards) => throw new PortPendingException("ChipSources.SourcesFromCards");

    /// <summary>The people written to in the given sent messages (newest first; To, then Cc, of each): one entry per address (case-insensitive; the first name and spelling win), blank addresses skipped (Bulwark loadRecentRecipients).</summary>
    public static IReadOnlyList<(string Name, string Email)> RecentFromSent(IReadOnlyList<EmailSummary> sent) => throw new PortPendingException("ChipSources.RecentFromSent");
}
