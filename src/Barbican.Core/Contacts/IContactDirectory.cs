// Mirrors: Rust contacts_x.rs:847 PRINCIPALS, :848 PRINCIPAL_PROPS, :858 fetch_directory (Principal/query with a Principal/get fallback when the server refuses the query), :850 directory_enabled; Bulwark components/contacts/directory-browser.tsx;
// RFC 9670 (JMAP Principals); Stalwart crates/jmap/src/principal/. B-CON-025; setting showContactsDirectory. Contract from the M5 plan section 4.4; owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>The server directory (the principals the login may see), read only.</summary>
public interface IContactDirectory
{
    /// <summary>Whether the login has the principals capability (the Directory entry and picker show only then).</summary>
    bool IsAvailable(string accountKey);

    /// <summary>All principals (id, type, name, email, description), paged by maxObjectsInGet capped at 500; the flag is true when the list came from the Principal/get fallback.</summary>
    Task<Result<(IReadOnlyList<JsonNode> Principals, bool Fallback)>> LoadAsync(string accountKey, CancellationToken ct);
}
