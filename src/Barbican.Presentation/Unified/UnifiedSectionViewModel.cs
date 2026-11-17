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

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _badge = "";

    [ObservableProperty]
    private bool _isOpen;

    public UnifiedSectionViewModel(UnifiedInbox unified, IUnifiedUnread others, IPrefsStore prefs, IDispatcher dispatcher, Func<MessageListViewModel> list)
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
        _prefs.Changed += OnChanged;
        _others.Changed += OnChanged;
        _unified.Refreshed += OnRefreshed;
        Update();
    }

    /// <summary>Opens the unified inbox in the list.</summary>
    public async Task OpenAsync()
    {
        IsOpen = true;
        await _list().OpenFolderAsync(UnifiedRows.FolderId, MailboxRole.None, Title).ConfigureAwait(true);
        Update();
    }

    public void Dispose()
    {
        _prefs.Changed -= OnChanged;
        _others.Changed -= OnChanged;
        _unified.Refreshed -= OnRefreshed;
    }

    /// <summary>The badge: the inboxes read by the unified view at their last read, else the other accounts' unread.</summary>
    public void Update()
    {
        IsVisible = _unified.Settings.Enabled;
        var read = _unified.Source.Unread;
        var total = read.Count > 0 ? read.Values.Where(v => v > 0).Sum() : _others.Total;
        Badge = total > 0 ? total.ToString(System.Globalization.CultureInfo.CurrentCulture) : "";
        IsOpen = IsOpen && _unified.Source.Query is not null;
    }

    private void OnChanged(object? sender, EventArgs e) => _dispatcher.Post(Update);

    private void OnRefreshed(object? sender, EventArgs e) => _dispatcher.Post(Update);
}
