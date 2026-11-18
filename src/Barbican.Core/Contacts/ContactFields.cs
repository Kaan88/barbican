// Mirrors: Rust contacts_x.rs:121 fields_of, :190 build_card, :94 date_text, :107 parse_date_text; Bulwark components/contacts/contact-form.tsx (entries kept with their unknown properties); B-CON-011, B-CON-012. Contract from the M5 plan section 4.1; owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>The form rows of a JSContact card and the card a saved form builds.</summary>
public static class ContactFields
{
    /// <summary>The entry rows of a card (emails, phones, addresses, dates, related people, links, messaging, ...), each remembering its key so a save keeps what the form does not show.</summary>
    public static IReadOnlyList<ContactField> Of(JsonNode card) => throw new PortPendingException("ContactFields.Of");

    /// <summary>The card after the form: the original with the edited entries merged in (unknown properties of an entry, the name components the form has no field for and every untouched property kept), new entries get the next free key, emptied single fields are removed (null), bad input (an address without an @, a date that is not a date) is a ValidationError.</summary>
    public static Result<JsonObject> Build(JsonNode? original, ContactForm form, IReadOnlyList<ContactField> fields) => throw new PortPendingException("ContactFields.Build");

    /// <summary>A JSContact PartialDate as the form shows it ("1815-12-10", "--06-01").</summary>
    public static string DateText(JsonNode date) => throw new PortPendingException("ContactFields.DateText");

    /// <summary>The form's date text (yyyy-MM-dd, --MM-dd, yyyy-MM) as a PartialDate object; a ValidationError for anything else ("31/12").</summary>
    public static Result<JsonObject> ParseDateText(string text) => throw new PortPendingException("ContactFields.ParseDateText");
}
