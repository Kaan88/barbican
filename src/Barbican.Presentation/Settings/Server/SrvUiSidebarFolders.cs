// Mirrors: M6 plan section 4.8 (the folder list comes from the sidebar's mailbox source). Owner srv-ui.

using System.ComponentModel;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Settings.Folders;
using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The sidebar's mailbox tree as the folders of the Folders page.</summary>
public sealed class SrvUiSidebarFolders : ISrvUiFolderSource
{
    private readonly SidebarViewModel _sidebar;

    public SrvUiSidebarFolders(SidebarViewModel sidebar)
    {
        ArgumentNullException.ThrowIfNull(sidebar);
        _sidebar = sidebar;
        _sidebar.PropertyChanged += OnSidebarChanged;
    }

    public event EventHandler? Changed;

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
