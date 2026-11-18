// Mirrors: Rust contacts_x.rs:352 pretty_date; Bulwark lib/contact-dates.ts. B-CON-013. Contract from the M5 plan section 4.1; owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>Display and arithmetic of the dates of a card.</summary>
public static class ContactDates
{
    /// <summary>English long form (d MMMM yyyy) and the days from <paramref name="today"/> to the next occurrence (today is 0).</summary>
    public static PrettyDate Pretty(JsonNode date, DateOnly today) => throw new PortPendingException("ContactDates.Pretty");
}
