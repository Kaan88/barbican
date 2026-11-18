// Not Rust rows: G-036..G-041, I-428..I-430 (Bulwark folder-settings.tsx; Stalwart mailbox/set.rs: one folder per role). M6 stream srv-data.

using Barbican.Core.Settings.Folders;

namespace Barbican.Core.Tests.Settings.Folders;

public sealed class FolderAdminRulesTests
{
    private static readonly FolderAdminNode[] Tree =
    [
        new("inbox", null, "Inbox", "inbox"),
        new("arch", null, "Archive", "archive"),
        new("work", null, "Work", null),
        new("proj", "work", "Projects", null),
        new("deep", "proj", "Deep", null),
    ];

    [Fact]
    [Red("srv-data")]
    public void Names_roles_and_holders()
    {
        Assert.Null(FolderAdminRules.NameProblem("Receipts", ["Work", "Archive"]));
        Assert.NotNull(FolderAdminRules.NameProblem("   ", []));
        Assert.NotNull(FolderAdminRules.NameProblem("a/b", []));
        Assert.NotNull(FolderAdminRules.NameProblem("work", ["Work"]));
        Assert.NotNull(FolderAdminRules.NameProblem(new string('x', 256), []));
        Assert.Equal("arch", FolderAdminRules.RoleHolder(Tree, "archive", null)?.Id);
        Assert.Null(FolderAdminRules.RoleHolder(Tree, "archive", "arch"));
        Assert.Null(FolderAdminRules.RoleHolder(Tree, "junk", null));
    }

    [Fact]
    [Red("srv-data")]
    public void A_folder_cannot_move_under_itself_and_role_folders_stay_put()
    {
        Assert.True(FolderAdminRules.CanMoveUnder(Tree, "deep", null));
        Assert.True(FolderAdminRules.CanMoveUnder(Tree, "proj", "arch"));
        Assert.False(FolderAdminRules.CanMoveUnder(Tree, "work", "work"));
        Assert.False(FolderAdminRules.CanMoveUnder(Tree, "work", "deep"));
        Assert.False(FolderAdminRules.CanMoveUnder(Tree, "inbox", "work"));
    }
}
