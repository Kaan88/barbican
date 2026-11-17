// Mirrors: Rust mail.rs build_rows :463 (headers), unread_first_size :582; Bulwark components/email/email-list.tsx:158,:355,:370 (unread-first
// sections "Unread" / "Everything else"), lib/list-date-groups.ts (sectionThreadsByDate). Owner mailcore.

using Barbican.Core.Mail.Grouping;
using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail;

/// <summary>Turns the loaded messages into the rows of the list: message rows with header rows between them.</summary>
public static class ListLayout
{
    /// <summary>
    /// The rows. With <paramref name="unreadFirst"/> (the section size) the Inbox shows the newest unread messages first under "Unread", then
    /// everything else; otherwise, with <paramref name="groupByDate"/>, a header opens each date group.
    /// </summary>
    public static IReadOnlyList<ListItemViewModel> Build(
        IReadOnlyList<EmailSummary> emails,
        RowContext context,
        bool groupByDate,
        int? unreadFirst,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? badges = null)
    {
        ArgumentNullException.ThrowIfNull(emails);
        ArgumentNullException.ThrowIfNull(context);
        var rows = new List<ListItemViewModel>(emails.Count + 8);
        if (unreadFirst is { } size)
        {
            var section = emails.Where(e => !e.Keywords.Seen).Take(size).ToList();
            var inSection = new HashSet<string>(section.Select(e => e.Id), StringComparer.Ordinal);
            if (section.Count > 0)
            {
                rows.Add(new GroupHeaderViewModel("Unread", "hdr:unread"));
                rows.AddRange(section.Select(e => Row(e, context, badges)));
            }

            rows.Add(new GroupHeaderViewModel("Everything else", "hdr:rest"));
            rows.AddRange(emails.Where(e => !inSection.Contains(e.Id)).Select(e => Row(e, context, badges)));
            return rows;
        }

        ListDateGroup? last = null;
        foreach (var email in emails)
        {
            if (groupByDate)
            {
                var group = email.ReceivedAt is { } at ? DateGroups.GroupOf(at, context.Now, context.Zone) : ListDateGroup.Older;
                if (last is null || last.Value != group)
                {
                    last = group;
                    rows.Add(new GroupHeaderViewModel(group.Label, $"hdr:{group.Kind}{group.Weekday}"));
                }
            }

            rows.Add(Row(email, context, badges));
        }

        return rows;
    }

    private static MessageRowViewModel Row(EmailSummary e, RowContext context, IReadOnlyDictionary<string, IReadOnlyList<string>>? badges) =>
        badges is not null && badges.TryGetValue(e.Id, out var b) ? new MessageRowViewModel(e, context, b) : new MessageRowViewModel(e, context);
}
