// Mirrors: Rust contacts_x.rs:380 detail_lines; Bulwark components/contacts/contact-detail.tsx. B-CON-013. Owner con-ui: the detail pane asks con-data's ContactDetail.Lines; while that is not implemented (M5 S1) it shows the same sections from the plain card.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Contacts;

namespace Barbican.Presentation.Contacts;

/// <summary>The lines of the detail pane.</summary>
public static class ContactsDetailLines
{
    public static IReadOnlyList<ContactLine> Of(JsonNode card, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(card);
        try
        {
            return ContactDetail.Lines(card, today);
        }
        catch (PortPendingException)
        {
            return Plain(card);
        }
    }

    private static List<ContactLine> Plain(JsonNode card)
    {
        var lines = new List<ContactLine>();
        Add(lines, "EMAIL", card["emails"], "address");
        Add(lines, "PHONE", card["phones"], "number");
        Add(lines, "ORGANIZATION", card["organizations"], "name");
        Add(lines, "NOTES", card["notes"], "note");
        if (card["addresses"] is JsonObject addresses)
        {
            foreach (var (_, a) in addresses)
            {
                var text = ContactsJson.Str(a, "full");
                if (text.Length == 0 && a?["components"] is JsonArray parts)
                {
                    text = string.Join(", ", parts.Select(p => ContactsJson.Str(p, "value")).Where(v => v.Length > 0));
                }

                if (text.Length > 0)
                {
                    lines.Add(new ContactLine("ADDRESS", string.Empty, text));
                }
            }
        }

        return lines;
    }

    private static void Add(List<ContactLine> lines, string section, JsonNode? map, string field)
    {
        if (map is not JsonObject o)
        {
            return;
        }

        foreach (var (_, entry) in o)
        {
            var value = ContactsJson.Str(entry, field);
            if (value.Length > 0)
            {
                lines.Add(new ContactLine(section, ContactsJson.Str(entry, "label"), value));
            }
        }
    }
}
