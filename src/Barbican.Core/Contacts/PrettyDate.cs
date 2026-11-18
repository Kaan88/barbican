// Mirrors: Rust contacts_x.rs:352 pretty_date (the long text and the days until the next occurrence). Contract from the M5 plan section 4.1; owner con-data.

namespace Barbican.Core.Contacts;

/// <summary>A date of a card for display: "10 December 1815" (no year: "20 November") and the days until it next falls (null when the date has no month or day).</summary>
public sealed record PrettyDate(string Text, int? DaysUntilNext);
