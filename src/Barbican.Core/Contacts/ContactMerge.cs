// Mirrors: Rust contact_merge.rs:19 name_key, :31 phone_key, :39 find_groups, :136 plan, :199 member_keys, :210 group_moves; Bulwark lib/contacts/merge.ts, components/contacts/contact-merge-dialog.tsx, contact-duplicates-view.tsx. B-CON-023, B-CON-024.
// Contract from the M5 plan section 4.1; owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>Finding and merging duplicate contacts.</summary>
public static class ContactMerge
{
    /// <summary>The comparison key of a card's name (lower-case, diacritics stripped, word order ignored: "Anna Müller" and "Muller, anna" match); empty without a name.</summary>
    public static string NameKey(JsonNode card) => throw new PortPendingException("ContactMerge.NameKey");

    /// <summary>The digits of a phone number without the country prefix and a leading zero, so "+49 30 5550000" and "030 5550000" match; empty with too few digits ("12").</summary>
    public static string PhoneKey(string number) => throw new PortPendingException("ContactMerge.PhoneKey");

    /// <summary>Groups of two or more card indexes that share a name key, an email or a phone key (transitively joined); groups (kind group) are ignored; each group is ascending and the groups are ordered by their first index.</summary>
    public static IReadOnlyList<IReadOnlyList<int>> FindGroups(IReadOnlyList<JsonNode> cards) => throw new PortPendingException("ContactMerge.FindGroups");

    /// <summary>Keeps the card with the newest <c>updated</c> (the first wins a tie or no date); the patch adds what the others have and the kept card lacks (emails and phones not already there case-insensitively, entries of every other map under a free key such as "p1m1", keywords, media) and takes a single field only when the kept card has none. Null for an empty list.</summary>
    public static ContactMergePlan? Plan(IReadOnlyList<JsonNode> cards) => throw new PortPendingException("ContactMerge.Plan");

    /// <summary>The keys under which a card is a group member: its uid, the uid with a "urn:uuid:" prefix removed (or added when it has none), and its id.</summary>
    public static IReadOnlyList<string> MemberKeys(JsonNode card) => throw new PortPendingException("ContactMerge.MemberKeys");

    /// <summary>For each group that has a merged-away card as a member: (group id, the new members map) with those cards replaced by the kept card.</summary>
    public static IReadOnlyList<(string GroupId, JsonObject Members)> GroupMoves(IReadOnlyList<JsonNode> groups, JsonNode keep, IReadOnlyList<JsonNode> others) => throw new PortPendingException("ContactMerge.GroupMoves");
}
