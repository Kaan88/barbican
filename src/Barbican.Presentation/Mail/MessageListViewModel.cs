// Mirrors: M1-plan §4.3 (Rows, Selected, Total, LoadMoreCommand, SearchText, unread-first Inbox section); spec I-119..I-156. Contract stub from F2; owner mailcore.

using System.Collections.ObjectModel;

namespace Barbican.Presentation.Mail;

/// <summary>The message list (the mailcore stream adds Selected, Total, LoadMoreCommand, SearchText and the unread-first section).</summary>
public sealed class MessageListViewModel
{
    public ObservableCollection<ListItemViewModel> Rows { get; } = [];
}
