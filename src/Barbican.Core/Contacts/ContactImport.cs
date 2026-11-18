// Mirrors: Rust vcard.rs:667 detect_duplicates, contacts_x.rs:752 import_batch, :170 new_uid, :909 principal_card; Bulwark components/contacts/contact-import-dialog.tsx, lib/contacts/. B-CON-016, B-CON-022. Contract from the M5 plan section 4.1; owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>Pure rules of the vCard import.</summary>
public static class ContactImport
{
    /// <summary>Indexes of incoming cards with an email (trimmed, case-insensitive) that is already known: in <paramref name="existing"/> or in an earlier incoming card.</summary>
    public static IReadOnlyList<int> DetectDuplicates(IReadOnlyList<JsonNode> existing, IReadOnlyList<JsonNode> incoming) => throw new PortPendingException("ContactImport.DetectDuplicates");

    /// <summary>The cards to create: duplicates left out when <paramref name="skipDuplicates"/>, every card in <paramref name="bookId"/>, the id removed, a uid ("urn:uuid:" and a GUID) when it has none.</summary>
    public static IReadOnlyList<JsonNode> Batch(IReadOnlyList<JsonNode> cards, IReadOnlyList<int> duplicates, bool skipDuplicates, string bookId) => throw new PortPendingException("ContactImport.Batch");

    /// <summary>"urn:uuid:" followed by a lower-case GUID (45 characters).</summary>
    public static string NewUid() => throw new PortPendingException("ContactImport.NewUid");

    /// <summary>A directory principal (id, type, name, email, description) as a read-only card: kind group or individual, one email, the description as a note.</summary>
    public static JsonNode PrincipalCard(JsonNode principal) => throw new PortPendingException("ContactImport.PrincipalCard");
}
