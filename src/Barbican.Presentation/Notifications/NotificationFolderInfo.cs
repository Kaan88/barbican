// Mirrors: Bulwark lib/desktop/new-mail.ts:44-47. The roles come from the active account's folder tree (MailboxRole), the mutes from the Inbox metadata through IFolderAdmin.PrefsAsync (G-041, srv-data); both are kept until a folder change. Owner notifications.

using System.Collections.Concurrent;
using Barbican.Core.Mail.Model;
using Barbican.Core.Settings.Folders;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Notifications;

/// <summary>The folder roles and mutes of the accounts, from the live services.</summary>
public sealed class NotificationFolderInfo : INotificationFolderInfo, IDisposable
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);
    private readonly IActiveMail _active;
    private readonly IFolderAdmin? _folders;
    private readonly ConcurrentDictionary<string, IReadOnlySet<string>> _muted = new(StringComparer.Ordinal);

    public NotificationFolderInfo(IActiveMail active, IFolderAdmin? folders = null)
    {
        ArgumentNullException.ThrowIfNull(active);
        _active = active;
        _folders = folders;
        if (_folders is not null)
        {
            _folders.Changed += OnChanged;
        }
    }

    public async Task<(IReadOnlyDictionary<string, string> RoleIds, IReadOnlySet<string> Muted)> GetAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.Equals(_active.AccountKey, accountKey, StringComparison.Ordinal) && _active.Mail is { } mail
            && await mail.GetMailboxesAsync(ct).ConfigureAwait(false) is { IsOk: true, Value: { } tree })
        {
            foreach (var box in tree.All.Where(m => m.Role != MailboxRole.None))
            {
                roles.TryAdd(box.Role.ToString().ToLowerInvariant(), box.Id);
            }
        }

        if (!_muted.TryGetValue(accountKey, out var muted))
        {
            muted = None;
            if (_folders is not null && await _folders.PrefsAsync(accountKey, ct).ConfigureAwait(false) is { IsOk: true, Value: { } prefs })
            {
                muted = prefs.Muted;
                _muted[accountKey] = muted;
            }
        }

        return (roles, muted);
    }

    public void Invalidate(string? accountKey = null)
    {
        if (accountKey is null)
        {
            _muted.Clear();
        }
        else
        {
            _muted.TryRemove(accountKey, out _);
        }
    }

    public void Dispose()
    {
        if (_folders is not null)
        {
            _folders.Changed -= OnChanged;
        }
    }

    private void OnChanged(object? sender, FolderAdminChangedEventArgs e) => Invalidate(e.AccountKey);
}
