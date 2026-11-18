// Mirrors: M6 plan section 3.5 (sample-data runs). A fixed folder list for BARBICAN_SAMPLE=1; the sample folder admin keeps preferences only, so this list does not change. Owner srv-ui.

using Barbican.Core.Settings.Folders;

namespace Barbican.Presentation.Settings.Server;

/// <summary>Sample folders for the sample-data run and the tests.</summary>
public sealed class SrvUiSampleFolders : ISrvUiFolderSource
{
    private static readonly FolderAdminNode[] Items =
    [
        new("f-inbox", null, "Inbox", "inbox"),
        new("f-drafts", null, "Drafts", "drafts"),
        new("f-sent", null, "Sent", "sent"),
        new("f-archive", null, "Archive", "archive"),
        new("f-junk", null, "Junk", "junk"),
        new("f-trash", null, "Trash", "trash"),
        new("f-projects", null, "Projects", null),
        new("f-alpha", "f-projects", "Alpha", null),
        new("f-beta", "f-projects", "Beta", null),
        new("f-receipts", null, "Receipts", null),
    ];

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<FolderAdminNode> Nodes() => Items;

    public bool Subscribed(string id) => !string.Equals(id, "f-receipts", StringComparison.Ordinal);

    public int Unread(string id) => string.Equals(id, "f-inbox", StringComparison.Ordinal) ? 3 : 0;
}
