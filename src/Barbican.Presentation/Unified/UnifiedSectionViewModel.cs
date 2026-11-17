// Mirrors: Bulwark components/layout/sidebar.tsx (the "All inboxes" row above the folders when enableUnifiedMailbox is on, its badge the
// unread of the inboxes read), Rust main.slint unified row and unified.rs:400 unread_total. spec I-050, I-051. Owner unified.

using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Unified;
using Barbican.Core.Storage;
using Barbican.Presentation.Common;
using Barbican.Presentation.Mail;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Unified;

/// <summary>The unified inbox row of the sidebar.</summary>
public sealed partial class UnifiedSectionViewModel : ObservableObject, IDisposable
{
    /// <summary>The row's title (and the list's title when it is open).</summary>
    public const string Title = "All inboxes";

    private readonly UnifiedInbox _unified;
    private readonly IUnifiedUnread _others;
    private readonly IPrefsStore _prefs;
    private readonly IDispatcher _dispatcher;
    private readonly Func<MessageListViewModel> _list;
    private readonly Func<Shell.GlanceTilesViewModel?> _glance;

    private MessageListViewModel? _watched;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _badge = "";

    /// <summary>The unified inbox reads every login (not only the one on screen): the per-account rows under the row show.</summary>
    [ObservableProperty]
    private bool _listsAccounts;

    [ObservableProperty]
    private bool _isOpen;

    public UnifiedSectionViewModel(UnifiedInbox unified, IUnifiedUnread others, IPrefsStore prefs, IDispatcher dispatcher, Func<MessageListViewModel> list, Func<Shell.GlanceTilesViewModel?>? glance = null)
    {
        ArgumentNullException.ThrowIfNull(unified);
        ArgumentNullException.ThrowIfNull(others);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(list);
        _unified = unified;
        _others = others;
        _prefs = prefs;
        _dispatcher = dispatcher;
        _list = list;
        _glance = glance ?? (() => null);
        _prefs.Changed += OnChanged;
        _others.Changed += OnChanged;
        _unified.Refreshed += OnRefreshed;
        Update();
    }

    /// <summary>Opens the unified inbox in the list.</summary>
    public async Task OpenAsync()
    {
        var list = _list();
        if (_watched is null)
        {
            // the list is the one truth for what is open: a folder row, a command or the palette that opens another folder ends "All inboxes" (unified review request)
            _watched = list;
            list.PropertyChanged += OnListChanged;
        }

        IsOpen = true;
        await list.OpenFolderAsync(UnifiedRows.FolderId, MailboxRole.None, Title).ConfigureAwait(true);
        Update();

        // the list header names the open folder (the sidebar sets it for a folder row)
        _glance()?.SetFolder(Title, (int)Math.Min(int.MaxValue, UnreadTotal()));
    }

    public void Dispose()
    {
        _prefs.Changed -= OnChanged;
        _others.Changed -= OnChanged;
        _unified.Refreshed -= OnRefreshed;
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnListChanged;
        }
    }

    private void OnListChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(MessageListViewModel.MailboxId), StringComparison.Ordinal) && _watched is { MailboxId: { } id })
        {
            IsOpen = string.Equals(id, UnifiedRows.FolderId, StringComparison.Ordinal);
        }
    }

    /// <summary>The badge: the inboxes read by the unified view at their last read, else the other accounts' unread.</summary>
    public void Update()
    {
        IsVisible = _unified.Settings.Enabled;
        ListsAccounts = _unified.Settings.Enabled && _unified.Settings.CrossAccount;
        var total = UnreadTotal();
        Badge = total > 0 ? total.ToString(System.Globalization.CultureInfo.CurrentCulture) : "";
        IsOpen = IsOpen && _unified.Source.Query is not null && string.Equals(_watched?.MailboxId, UnifiedRows.FolderId, StringComparison.Ordinal);
    }

    private long UnreadTotal()
    {
        var read = _unified.Source.Unread;
        return read.Count > 0 ? read.Values.Where(v => v > 0).Sum() : _others.Total;
    }

    private void OnChanged(object? sender, EventArgs e) => _dispatcher.Post(Update);

    private void OnRefreshed(object? sender, EventArgs e) => _dispatcher.Post(Update);
}
