// Not Rust rows: I-430 (Bulwark folder-settings.tsx: the folder tree). M6 stream srv-ui.

using Barbican.Core.Settings.Folders;
using Barbican.Presentation.Settings.Server;

namespace Barbican.Presentation.Tests.Settings.Server;

public sealed class SrvUiFolderTreeRulesTests
{
    private static readonly FolderAdminNode[] Folders =
    [
        new("zed", null, "Zed", null),
        new("trash", null, "Trash", "trash"),
        new("inbox", null, "Inbox", "inbox"),
        new("alpha", null, "alpha", null),
        new("kid2", "alpha", "b child", null),
        new("kid1", "alpha", "A child", null),
        new("sent", null, "Sent", "sent"),
    ];

    [Fact]
    public void Role_folders_come_first_then_the_rest_by_name_with_children_under_their_parent()
    {
        var rows = SrvUiFolderTreeRules.Flatten(Folders, new HashSet<string>(StringComparer.Ordinal));
        Assert.Equal("inbox,sent,trash,alpha,kid1,kid2,zed", string.Join(',', rows.Select(r => r.Node.Id)));
        Assert.Equal([0, 0, 0, 0, 1, 1, 0], rows.Select(r => r.Depth));
        Assert.True(rows.Single(r => string.Equals(r.Node.Id, "alpha", StringComparison.Ordinal)).HasChildren);
    }

    [Fact]
    public void A_collapsed_parent_hides_its_children()
    {
        var rows = SrvUiFolderTreeRules.Flatten(Folders, new HashSet<string>(StringComparer.Ordinal) { "alpha" });
        Assert.DoesNotContain(rows, r => r.Node.Id.StartsWith("kid", StringComparison.Ordinal));
        Assert.True(rows.Single(r => string.Equals(r.Node.Id, "alpha", StringComparison.Ordinal)).Collapsed);
    }
}
