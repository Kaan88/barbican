// Mirrors: M6 plan section 4.8 (the folder list comes from the sidebar's mailbox source). Owner srv-ui.

using System.ComponentModel;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Settings.Folders;
using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The sidebar's mailbox tree as the folders of the Folders page.</summary>
public sealed class SrvUiSidebarFolders : ISrvUiFolderSource
{
    private readonly SidebarViewModel _sidebar;
    private readonly IAccountManager _accounts;

    public SrvUiSidebarFolders(SidebarViewModel sidebar, IAccountManager accounts)
    {
        ArgumentNullException.ThrowIfNull(sidebar);
        ArgumentNullException.ThrowIfNull(accounts);
        _sidebar = sidebar;
        _accounts = accounts;
        _sidebar.PropertyChanged += OnSidebarChanged;
    }

    public event EventHandler? Changed;

    public async Task<Result<IReadOnlyList<SrvUiFolderSnapshot>>> LoadAsync(string accountKey, CancellationToken ct)
    {
        var session = _accounts.SessionFor(accountKey);
        if (session is null) return Result<IReadOnlyList<SrvUiFolderSnapshot>>.Fail(new ValidationError("Sign in to an account first."));
        var result = await session.Mail.GetMailboxesAsync(ct).ConfigureAwait(false);
        if (!result.IsOk) return Result<IReadOnlyList<SrvUiFolderSnapshot>>.Fail(result.Error!);
        return Result<IReadOnlyList<SrvUiFolderSnapshot>>.Ok([.. result.Value!.All.Select(m =>
            new SrvUiFolderSnapshot(new FolderAdminNode(m.Id, m.ParentId, m.Name, MailboxRoleConverter.Name(m.Role)), m.UnreadEmails, m.IsSubscribed))]);
    }

    public IReadOnlyList<FolderAdminNode> Nodes() => [.. _sidebar.Tree.All.Select(m => new FolderAdminNode(m.Id, m.ParentId, m.Name, MailboxRoleConverter.Name(m.Role)))];

    public int Unread(string id) => _sidebar.Tree.All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal))?.UnreadEmails ?? 0;

    public bool Subscribed(string id) => _sidebar.Tree.All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal))?.IsSubscribed ?? true;

    private void OnSidebarChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SidebarViewModel.InboxUnread), StringComparison.Ordinal))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
