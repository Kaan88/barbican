// New tests: the folder tree, tags and storage meter. Spec I-038..I-063, J-046 (unsubscribed folders muted), B-MAIL-017. Owner mailcore.

#pragma warning disable MA0002, VSTHRD003 // asserts compare strings ordinally by nature; fakes hand back TaskCompletionSource tasks on purpose
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Common;
using Barbican.Presentation.Shell;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Shell;

public sealed class SidebarViewModelTests
{
    private static Mailbox Box(string id, string name, MailboxRole role = MailboxRole.None, string? parent = null, int unread = 0, int total = 0, bool subscribed = true, int order = 0) =>
        new() { Id = id, Name = name, Role = role, ParentId = parent, UnreadEmails = unread, TotalEmails = total, IsSubscribed = subscribed, SortOrder = order };

    private static MailboxTree Tree() => new(
    [
        Box("w", "Work", parent: null, unread: 0, total: 5),
        Box("sent", "Sent Items", MailboxRole.Sent, total: 9),
        Box("inbox", "INBOX", MailboxRole.Inbox, unread: 3, total: 40),
        Box("c", "Clients", parent: "w", unread: 2, total: 8, subscribed: false),
        Box("junk", "Junk Mail", MailboxRole.Junk, unread: 1, total: 4),
    ]);

    [Fact]
    public void Roles_come_first_with_their_own_names_and_counts()
    {
        using var h = new ListHarness();
        h.Sidebar.Update(Tree());
        Assert.Equal(["Inbox", "Sent", "Spam", "Work", "Clients"], h.Sidebar.Folders.Select(f => f.Name));
        Assert.Equal(["3", "9", "1 / 4", "5", "2"], h.Sidebar.Folders.Select(f => f.Count));
        Assert.Equal([0, 0, 0, 0, 1], h.Sidebar.Folders.Select(f => f.Depth));
        Assert.Equal(3, h.Sidebar.InboxUnread);
    }

    [Fact]
    public void An_unsubscribed_folder_is_muted_and_a_parent_has_a_chevron()
    {
        using var h = new ListHarness();
        h.Sidebar.Update(Tree());
        Assert.True(h.Sidebar.Folders.Single(f => string.Equals(f.Id, "c", StringComparison.Ordinal)).Muted);
        Assert.Equal(1, h.Sidebar.Folders.Single(f => string.Equals(f.Id, "w", StringComparison.Ordinal)).Chevron);
    }

    [Fact]
    public void A_collapsed_parent_hides_its_children_and_shows_a_dot_when_unread_is_below()
    {
        using var h = new ListHarness();
        h.Sidebar.Update(Tree());
        h.Sidebar.ToggleFolder("w");
        Assert.DoesNotContain(h.Sidebar.Folders, f => string.Equals(f.Id, "c", StringComparison.Ordinal));
        var work = h.Sidebar.Folders.Single(f => string.Equals(f.Id, "w", StringComparison.Ordinal));
        Assert.Equal(2, work.Chevron);
        Assert.True(work.ChildUnread);
        h.Sidebar.ToggleFolder("w");
        Assert.Contains(h.Sidebar.Folders, f => string.Equals(f.Id, "c", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Arrow_right_unfolds_and_arrow_left_folds_the_selected_folder()
    {
        // Bulwark sidebar.tsx:2375, spec I-060
        using var h = new ListHarness();
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Core.Common.Result<ListPage>.Ok(Mails.Page(0, 2, 2, false)));
        h.Sidebar.Update(Tree());
        await h.Sidebar.SelectFolderAsync("w");
        Assert.False(h.Sidebar.SetSelectedExpanded(true));
        Assert.True(h.Sidebar.SetSelectedExpanded(false));
        Assert.DoesNotContain(h.Sidebar.Folders, f => string.Equals(f.Id, "c", StringComparison.Ordinal));
        Assert.False(h.Sidebar.SetSelectedExpanded(false));
        Assert.True(h.Sidebar.SetSelectedExpanded(true));
        Assert.Contains(h.Sidebar.Folders, f => string.Equals(f.Id, "c", StringComparison.Ordinal));
        await h.Sidebar.SelectFolderAsync("inbox");
        Assert.False(h.Sidebar.SetSelectedExpanded(false));
    }

    [Fact]
    public void A_refresh_with_the_same_folders_touches_nothing_and_a_count_change_sets_one_row()
    {
        using var h = new ListHarness();
        h.Sidebar.Update(Tree());
        h.Sidebar.Update(Tree());
        Assert.True(h.Sidebar.LastFolderSync.Untouched);
        var changed = Tree().All.Select(m => string.Equals(m.Id, "inbox", StringComparison.Ordinal) ? m with { UnreadEmails = 4 } : m).ToList();
        h.Sidebar.Update(new MailboxTree(changed));
        Assert.Equal(new SyncStats(Set: 1), h.Sidebar.LastFolderSync);
        Assert.Equal(4, h.Sidebar.InboxUnread);
    }

    [Fact]
    public void The_unread_style_setting_turns_counts_into_a_dot_or_nothing()
    {
        using var h = new ListHarness("""{"unreadCountStyle": "dot"}""");
        h.Sidebar.Update(Tree());
        Assert.Equal("●", h.Sidebar.Folders.First().Count);
        h.Prefs.Set("""{"unreadCountStyle": "hidden"}""");
        Assert.Equal(string.Empty, h.Sidebar.Folders.First().Count);
    }

    [Fact]
    public async Task Selecting_a_folder_opens_it_in_the_list_and_roles_can_be_reached_by_the_g_keys()
    {
        using var h = new ListHarness();
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Core.Common.Result<ListPage>.Ok(Mails.Page(0, 2, 2, false)));
        h.Sidebar.Update(Tree());
        await h.Sidebar.SelectFolderAsync("sent");
        Assert.Equal("sent", h.List.MailboxId);
        Assert.Equal("sent", h.Sidebar.SelectedFolderId);
        Assert.True(h.Sidebar.GoToRole(MailboxRole.Inbox));
        Assert.Equal("inbox", h.List.MailboxId);
        Assert.False(h.Sidebar.GoToRole(MailboxRole.Drafts));
    }

    [Fact]
    public async Task Initial_folder_is_highlighted_when_the_tree_arrives_after_the_list()
    {
        using var h = new ListHarness();
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        h.Sidebar.Update(Tree());
        Assert.Equal("inbox", h.Sidebar.SelectedFolderId);
    }

    [Fact]
    public async Task The_highlight_follows_the_list_not_only_the_folder_rows()
    {
        // unified review request: "All inboxes" clears the folder selection, and opening a folder by any path (a command, the palette) brings it back
        using var h = new ListHarness();
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Core.Common.Result<ListPage>.Ok(Mails.Page(0, 2, 2, false)));
        h.Sidebar.Update(Tree());
        await h.Sidebar.SelectFolderAsync("inbox");
        Assert.Equal("inbox", h.Sidebar.SelectedFolderId);
        await h.List.OpenFolderAsync(Core.Mail.Unified.UnifiedRows.FolderId, MailboxRole.None, "All inboxes");
        Assert.Null(h.Sidebar.SelectedFolderId);
        Assert.True(h.Sidebar.UnifiedOpen);
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        Assert.Equal("inbox", h.Sidebar.SelectedFolderId);
        Assert.False(h.Sidebar.UnifiedOpen);
    }

    [Fact]
    public void Tags_follow_the_settings_and_nest_with_a_slash()
    {
        using var h = new ListHarness("""{"nestedTags": true, "emailKeywords": [{"id":"work","label":"Work","color":"green"},{"id":"work/clients","label":"Clients","color":"blue"}]}""");
        Assert.Equal(["Work", "Clients"], h.Sidebar.Tags.Select(t => t.Label));
        Assert.Equal([0, 1], h.Sidebar.Tags.Select(t => t.Depth));
        Assert.Equal(0x22c55eu, h.Sidebar.Tags[0].ColorRgb);
        h.Sidebar.ToggleTag("work");
        Assert.Single(h.Sidebar.Tags);
        Assert.Equal(2, h.Sidebar.Tags[0].Chevron);
    }

    [Fact]
    public void The_colour_blind_palette_changes_tag_and_role_colours()
    {
        using var h = new ListHarness("""{"colorBlindPalette": true, "colorfulSidebarIcons": true}""");
        h.Sidebar.Update(Tree());
        Assert.Equal(0xD55E00u, h.Sidebar.Tags.First(t => string.Equals(t.Id, "red", StringComparison.Ordinal)).ColorRgb);
        Assert.NotEqual(0x818cf8u, h.Sidebar.Folders.First().ColorRgb);
    }

    [Fact]
    public void The_storage_meter_reads_the_quota()
    {
        using var h = new ListHarness();
        h.Sidebar.SetQuota(new QuotaInfo(1_200_000_000, 15_000_000_000));
        Assert.Equal("1.2 GB of 15 GB", h.Sidebar.StorageText);
        Assert.Equal(0.08, h.Sidebar.StorageFraction, 3);
        h.Sidebar.SetQuota(null);
        Assert.Equal(string.Empty, h.Sidebar.StorageText);
        Assert.Equal(-1, h.Sidebar.StorageFraction);
    }

    [Fact]
    public void The_rail_badge_follows_the_inbox_and_the_unread_style()
    {
        using var h = new ListHarness();
        using var rail = new RailViewModel(h.Shell, h.Sidebar, h.Active);
        h.Sidebar.Update(Tree());
        Assert.Equal("3", rail.MailBadge);
        h.Sidebar.Update(new MailboxTree([Box("inbox", "Inbox", MailboxRole.Inbox, unread: 150)]));
        Assert.Equal("99+", rail.MailBadge);
        h.Prefs.Set("""{"unreadCountStyle": "hidden"}""");
        Assert.Equal(string.Empty, rail.MailBadge);
        Assert.Equal("US", rail.AccountInitials);
    }
}
