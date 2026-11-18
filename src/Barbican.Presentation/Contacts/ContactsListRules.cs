// Mirrors: Rust contacts.rs:83 sort_name, full_name, cal_editor.rs:69 initials; Bulwark components/contacts/contact-list.tsx (getContactSortName, letter groups, search). B-CON-004, B-CON-027, B-CON-030. Contract from the M5 plan section 4.5; owner con-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Presentation.Contacts;

/// <summary>The pure rules of the contact list.</summary>
public static class ContactsListRules
{
    /// <summary>The display name: the full name, else the components joined (prefix, given, middle, surname, suffix), else the first email, else the nickname or organization.</summary>
    public static string FullName(JsonNode card) => throw new PortPendingException("ContactsListRules.FullName");

    /// <summary>The sort key: the display name, or with <paramref name="byLast"/> "Surname, given middle" from the name components (surname and surname2; given, given2, middle, additional), or from the last word of a plain full name ("Ada Lovelace" is "Lovelace, Ada").</summary>
    public static string SortName(JsonNode card, bool byLast) => throw new PortPendingException("ContactsListRules.SortName");

    /// <summary>The letter header of a sort key: its first letter upper-case with diacritics stripped, "#" for a digit or symbol.</summary>
    public static string LetterOf(string sortKey) => throw new PortPendingException("ContactsListRules.LetterOf");

    /// <summary>Avatar initials without a photo: two letters of the first two words of the name (words split at space, dot, underscore, hyphen), the first two letters of a single word, the part of the email before the @ when there is no name, "?" for nothing; upper-case.</summary>
    public static string Initials(string name, string email) => throw new PortPendingException("ContactsListRules.Initials");

    /// <summary>Whether a card matches the search text (every word of the text in the name, an email, a phone, an organization or a note; case and diacritics ignored).</summary>
    public static bool Matches(JsonNode card, string query) => throw new PortPendingException("ContactsListRules.Matches");
}
