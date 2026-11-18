// Mirrors: Rust contacts_x.rs:380 detail_lines; Bulwark components/contacts/contact-detail.tsx. B-CON-013. Contract from the M5 plan section 4.1; owner con-data (con-ui draws the lines).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>The detail pane's lines of a card, in display order.</summary>
public static class ContactDetail
{
    public static IReadOnlyList<ContactLine> Lines(JsonNode card, DateOnly today) => throw new PortPendingException("ContactDetail.Lines");
}
