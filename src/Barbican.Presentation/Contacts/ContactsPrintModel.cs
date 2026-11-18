// Mirrors: Bulwark components/contacts/contact-print*.tsx (a printable card per contact: name, organization, then the detail sections); G-010; D-M5-12 (an in-app print model with an in-window preview; the OS print path is built but never run by an agent).
// Pure: the pages are data; the App draws them on white paper in black ink and hands the same pages to the print dialog. Owner con-ui.

#pragma warning disable MA0048 // three small records of one print family live in one file
using System.Text.Json.Nodes;
using Barbican.Core.Contacts;

namespace Barbican.Presentation.Contacts;

/// <summary>One contact on a printed page: a heading and the lines under it.</summary>
public sealed record ContactsPrintBlock(string Name, string Subtitle, IReadOnlyList<ContactLine> Lines);

/// <summary>One printed page.</summary>
public sealed record ContactsPrintPage(IReadOnlyList<ContactsPrintBlock> Blocks);

/// <summary>What the preview shows and the printer prints.</summary>
public sealed record ContactsPrintDocument(string Title, IReadOnlyList<ContactsPrintPage> Pages);

/// <summary>Builds the pages of a contact print (one contact fills its page; a list puts several on a page).</summary>
public static class ContactsPrintModel
{
    /// <summary>The most lines (headings and values) one page carries before the next contact starts a new page.</summary>
    public const int LinesPerPage = 34;

    public static ContactsPrintDocument Build(IReadOnlyList<JsonNode> cards, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var pages = new List<ContactsPrintPage>();
        var current = new List<ContactsPrintBlock>();
        var used = 0;
        foreach (var card in cards)
        {
            var block = new ContactsPrintBlock(ContactsListRules.FullName(card), ContactsJson.FirstOf(card["organizations"], "name"), ContactsDetailLines.Of(card, today));
            var size = 3 + block.Lines.Count;
            if (current.Count > 0 && used + size > LinesPerPage)
            {
                pages.Add(new ContactsPrintPage(current));
                current = [];
                used = 0;
            }

            current.Add(block);
            used += size;
        }

        if (current.Count > 0)
        {
            pages.Add(new ContactsPrintPage(current));
        }

        var title = cards.Count == 1 ? ContactsListRules.FullName(cards[0]) : "Contacts";
        return new ContactsPrintDocument(title, pages);
    }
}
