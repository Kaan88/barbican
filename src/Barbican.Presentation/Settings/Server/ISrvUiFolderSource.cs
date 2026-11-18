// Mirrors: M6 plan section 4.8 ("the folder list itself comes from the sidebar's mailbox source"). Owner srv-ui.

using Barbican.Core.Settings.Folders;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The folders of the active account as the Folders page and the rule editor see them.</summary>
public interface ISrvUiFolderSource
{
    /// <summary>The folders now (role as the JMAP role name, lower case).</summary>
    IReadOnlyList<FolderAdminNode> Nodes();

    /// <summary>The unread count of a folder, or 0.</summary>
    int Unread(string id);

    /// <summary>Whether the folder is subscribed (shown in the folder list of mail clients).</summary>
    bool Subscribed(string id);

    /// <summary>The folders changed (raised on any thread).</summary>
    event EventHandler? Changed;
}
