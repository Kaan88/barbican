// Mirrors: Bulwark components/mail/mail-app.tsx:6733-6737 (the results line shows while a search or filter is active and nothing loads), components/email/search-result-count.tsx:14-31. Owner search.

namespace Barbican.Presentation.Mail;

public sealed partial class MessageListViewModel
{
    /// <summary>The count line above search results; null unless a search is showing and the list is not loading (mail-app.tsx:6733).</summary>
    public string? SearchCountText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_query?.SearchText) || IsLoading || IsConnecting)
            {
                return null;
            }

            var rows = Messages.ToList();
            var threading = Loading.MessageLoadRules.Threading(_prefs.Current);
            var messages = threading ? rows.Sum(r => Math.Max(r.ThreadCount, 1)) : rows.Count;
            return SearchResultCount.Text(rows.Count, messages, CanLoadMore, threading);
        }
    }
}
