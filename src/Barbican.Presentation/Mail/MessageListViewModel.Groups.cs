// Mirrors: Bulwark components/email/email-list.tsx:421-466 (folded date groups are kept for the view they were folded in; another folder, search or tag opens with every group open),
// components/email/list-date-group-header.tsx:14-37. ML-05.

namespace Barbican.Presentation.Mail;

public sealed partial class MessageListViewModel
{
    private readonly HashSet<string> _folded = new(StringComparer.Ordinal);
    private string _foldView = string.Empty;

    /// <summary>Folds or opens a date group by its header key; the rows are rebuilt at once.</summary>
    public void ToggleGroup(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (!_folded.Remove(key))
        {
            _folded.Add(key);
        }

        RebuildRows();
    }

    /// <summary>The folds belong to one view: a different folder, tag or search forgets them (email-list.tsx viewKey).</summary>
    private void ForgetFoldsOfOtherViews()
    {
        var view = string.Join('\u001f', _query?.MailboxId, TagId, _query?.SearchText);
        if (!string.Equals(view, _foldView, StringComparison.Ordinal))
        {
            _foldView = view;
            _folded.Clear();
        }
    }
}
