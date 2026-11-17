// Mirrors: Bulwark components/email/list-date-group-header.tsx and the "Unread" / "Everything else" section headers of email-list.tsx:158;
// Rust mail.rs build_rows :479. Owner mailcore.

namespace Barbican.Presentation.Mail;

/// <summary>A header row: a date group or a section of the unread-first Inbox.</summary>
public sealed class GroupHeaderViewModel : ListItemViewModel
{
    public GroupHeaderViewModel(string title)
        : this(title, "hdr:" + title)
    {
    }

    public GroupHeaderViewModel(string title, string key)
    {
        Title = title;
        Key = key;
    }

    public string Title { get; }

    public override string Key { get; }

    public override bool SameAs(ListItemViewModel other) =>
        other is GroupHeaderViewModel h && string.Equals(h.Title, Title, StringComparison.Ordinal);
}
