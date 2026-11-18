// Mirrors: Rust cal_editor.rs:69 initials, :735 parse_person, cal_extra.rs:53 people_matching, :84 birthday_parts is in BirthdayCalendar; Bulwark components/calendar/participant-input.tsx, lib/calendar-participants.ts.
// D-M4-06: the Rust code kept the contact cards in a thread-local; here they are an argument. Contract from the M4 plan section 4.3; owner cal-edit replaces the bodies.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>Guests as text: parsing, initials and the contact search.</summary>
public static class CalendarPeople
{
    /// <summary>"Ann Lee &lt;ann@x.example&gt;" and a bare "bob@y.example" parse; "not an address" and a dotless host ("a@b") give null.</summary>
    public static CalendarPerson? Parse(string text) => throw new PortPendingException("CalendarPeople.Parse");

    /// <summary>"AL" for "Ann Lee"; from the address ("bob.smith@y.example" gives "BS") when there is no name.</summary>
    public static string Initials(string name, string email) => throw new PortPendingException("CalendarPeople.Initials");

    /// <summary>Contacts and groups (JSContact cards) whose name or address contains <paramref name="query"/>, ranked name first, without the addresses in <paramref name="have"/>; a group lists its member cards.</summary>
    public static IReadOnlyList<PersonMatch> Matching(IReadOnlyList<JsonNode> cards, string query, IReadOnlyList<string> have, int max) => throw new PortPendingException("CalendarPeople.Matching");
}
