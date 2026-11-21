// Mirrors: Rust main.rs start-up (load mailboxes, open Inbox, identities, quota, flagged tile) and glance.rs refresh :93; hooks onAppReady, onInterval
// (Bulwark plugin-app-events.tsx:79, once a minute). Owner mailcore.

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;

namespace Barbican.Presentation.Shell;

/// <summary>Fills the shell once the window exists and again after every account switch: the folders, the Inbox, the identities, the quota and the flagged tile.</summary>
public sealed class MailShellStartup : IStartupTask, IDisposable
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private readonly IActiveMail _active;
    private readonly SidebarViewModel _sidebar;
    private readonly GlanceTilesViewModel _glance;
    private readonly ShellViewModel _shell;
    private readonly TimeProvider _time;
    private readonly ReaderDepth? _readerDepth;
    private ITimer? _interval;
    private CancellationTokenSource? _cts;

    public MailShellStartup(IActiveMail active, SidebarViewModel sidebar, GlanceTilesViewModel glance, ShellViewModel shell, TimeProvider time, ReaderDepth? readerDepth = null)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(sidebar);
        ArgumentNullException.ThrowIfNull(glance);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(time);
        _active = active;
        _sidebar = sidebar;
        _glance = glance;
        _shell = shell;
        _time = time;
        _readerDepth = readerDepth;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _sidebar.PropertyChanged += OnSidebarChanged;
        _active.Changed += OnAccountChanged;
        _interval = _time.CreateTimer(_ => _shell.NotifyInterval(), null, Minute, Minute);
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Loads (or reloads) everything of the active account and opens its Inbox.</summary>
    public async Task LoadAsync(CancellationToken ct)
    {
        var old = _cts;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        old?.Cancel();
        old?.Dispose();
        var token = _cts.Token;
        var mail = _active.Mail;
        if (mail is null)
        {
            return;
        }

        var boxes = await mail.GetMailboxesAsync(token).ConfigureAwait(true);
        if (token.IsCancellationRequested)
        {
            return;
        }

        if (boxes.IsOk && boxes.Value is { } tree)
        {
            _sidebar.Update(tree);
            _shell.SetUnreadBadge(_sidebar.InboxUnread);
            if (tree.All.FirstOrDefault(m => m.Role == MailboxRole.Inbox) is { } inbox)
            {
                await _sidebar.SelectFolderAsync(inbox.Id).ConfigureAwait(true);
            }
        }

        _shell.NotifyReady(_active.AccountKey);
        await Task.WhenAll(LoadIdentitiesAsync(mail, _active.AccountKey, token), LoadQuotaAsync(mail, token), LoadFlaggedAsync(mail, token)).ConfigureAwait(true);
    }

    public void Dispose()
    {
        _sidebar.PropertyChanged -= OnSidebarChanged;
        _active.Changed -= OnAccountChanged;
        _interval?.Dispose();
        _interval = null;

        // idempotent: the container disposes the instance once per registration that returns it
        var cts = _cts;
        _cts = null;
        cts?.Cancel();
        cts?.Dispose();
    }

    private async Task LoadIdentitiesAsync(Core.Mail.IMailService mail, string? accountKey, CancellationToken ct)
    {
        var r = await mail.GetIdentitiesAsync(ct).ConfigureAwait(true);
        if (r.IsOk && r.Value is { } identities && !ct.IsCancellationRequested)
        {
            _glance.SetIdentities(identities);
            // G-051: the reader's External chip judges by these from the first message on (Bulwark loads the auth store's identities at sign-in)
            if (accountKey is { } key)
            {
                _readerDepth?.SetIdentities(key, identities.Select(i => i.Email));
            }
        }
    }

    private async Task LoadQuotaAsync(Core.Mail.IMailService mail, CancellationToken ct)
    {
        var r = await mail.GetQuotaAsync(ct).ConfigureAwait(true);
        if (r.IsOk && !ct.IsCancellationRequested)
        {
            _sidebar.SetQuota(r.Value);
        }
    }

    private async Task LoadFlaggedAsync(Core.Mail.IMailService mail, CancellationToken ct)
    {
        // the flagged tile asks the list engine for the starred messages (keyword filter $flagged, RFC 8621 4.4.1 hasKeyword); a server that cannot answer leaves the tile empty
        var r = await mail.QueryAsync(new ListQuery { HasKeyword = "$flagged", PageSize = 3 }, null, ct).ConfigureAwait(true);
        if (r.IsOk && r.Value is { } page && !ct.IsCancellationRequested)
        {
            _glance.SetFlagged(page.Total ?? page.Items.Count, page.Items);
        }
    }

    private void OnAccountChanged(object? sender, EventArgs e) => _ = LoadAsync(CancellationToken.None);

    private void OnSidebarChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SidebarViewModel.InboxUnread), StringComparison.Ordinal))
        {
            _shell.SetUnreadBadge(_sidebar.InboxUnread);
        }

        if (string.Equals(e.PropertyName, nameof(SidebarViewModel.SelectedFolderId), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(SidebarViewModel.SelectedTagId), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(SidebarViewModel.InboxUnread), StringComparison.Ordinal))
        {
            // starring or unstarring happens in some folder; the Flagged tile counts across all of them, so it is asked again whenever the view changes (UI-SWEEP-1 G-01)
            if (_active.Mail is { } mail && _cts is { } cts && !cts.IsCancellationRequested)
            {
                _ = LoadFlaggedAsync(mail, cts.Token);
            }

            if (_sidebar.SelectedTagId is { } tagId)
            {
                // the tag view names the header with the tag
                _glance.SetFolder(_sidebar.Tags.FirstOrDefault(t => string.Equals(t.Id, tagId, StringComparison.Ordinal))?.Label ?? tagId, 0);
                return;
            }

            if (_sidebar.UnifiedOpen)
            {
                // "All inboxes" names the header itself (UnifiedSectionViewModel.OpenAsync); no folder row is selected, and the header keeps its title
                return;
            }

            var folder = _sidebar.Folders.FirstOrDefault(f => string.Equals(f.Id, _sidebar.SelectedFolderId, StringComparison.Ordinal));
            _glance.SetFolder(folder?.Name ?? string.Empty, folder?.Unread ?? 0);
        }
    }
}
