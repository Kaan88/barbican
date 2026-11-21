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
        : this(title, key, foldable: false, collapsed: false)
    {
    }

    /// <summary>A date group header that folds (list-date-group-header.tsx:14-37, email-list.tsx:421-428): <paramref name="collapsed"/> hides its rows.</summary>
    public GroupHeaderViewModel(string title, string key, bool foldable, bool collapsed)
    {
        Title = title;
        Key = key;
        Foldable = foldable;
        Collapsed = collapsed;
    }

    /// <summary>The header carries the fold chevron (date groups; the unread-first sections do not fold here).</summary>
    public bool Foldable { get; }

    /// <summary>The group is folded: the header shows a right chevron and its rows are left out.</summary>
    public bool Collapsed { get; }

    public string Title { get; }

    public override string Key { get; }

    public override bool SameAs(ListItemViewModel other) =>
        other is GroupHeaderViewModel h && string.Equals(h.Title, Title, StringComparison.Ordinal) && h.Collapsed == Collapsed && h.Foldable == Foldable;
}
