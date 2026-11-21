// Mirrors: Rust mail.rs rebuild_folders :115 (walk, role names :136, count_text :107, role_icon :90, role_color :97, role_rank :104, unread style :170),
// Bulwark lib/mailbox-label.ts (role names), components/layout/sidebar.tsx; spec I-038..I-046, J-046 (unsubscribed folders muted). Owner mailcore.

using System.Globalization;
using Barbican.Core.Mail.Model;
using Barbican.Core.Settings.Accessibility;

namespace Barbican.Presentation.Mail;

/// <summary>Builds the sidebar's folder rows from the mailbox tree.</summary>
public static class FolderRows
{
    /// <summary>The tree flattened in display order: roles first (Inbox, Drafts, Sent, Archive, Spam, Trash), then the user's folders; children of a collapsed folder left out.</summary>
    public static IReadOnlyList<FolderRowViewModel> Build(MailboxTree tree, ISet<string> collapsed, FolderRowOptions options)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(collapsed);
        ArgumentNullException.ThrowIfNull(options);
        var known = new HashSet<string>(tree.All.Select(m => m.Id), StringComparer.Ordinal);
        var children = tree.All
            .GroupBy(m => m.ParentId is { } p && known.Contains(p) ? p : string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => Rank(m.Role)).ThenBy(m => m.SortOrder).ThenBy(m => m.Name.ToLowerInvariant(), StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var output = new List<FolderRowViewModel>();
        Walk(string.Empty, 0, children, collapsed, output, options);
        return output;
    }

    /// <summary>The Inbox's unread count (0 without one).</summary>
    public static int InboxUnread(MailboxTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return tree.All.FirstOrDefault(m => m.Role == MailboxRole.Inbox)?.UnreadEmails ?? 0;
    }

    /// <summary>The name a role shows (the server always names system folders in English).</summary>
    public static string DisplayName(Mailbox m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return m.Role switch
        {
            MailboxRole.Inbox => "Inbox",
            MailboxRole.Sent => "Sent",
            MailboxRole.Trash => "Trash",
            MailboxRole.Junk => "Spam",
            MailboxRole.Drafts => "Drafts",
            MailboxRole.Archive => "Archive",
            _ => m.Name,
        };
    }

    /// <summary>"3", "3 / 40", a dot or nothing, as the unread style and the totals setting say.</summary>
    public static string CountText(MailboxRole role, int unread, int total, bool showTotals, string unreadStyle)
    {
        string text;
        if (!showTotals)
        {
            text = unread > 0 ? unread.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }
        else if (unread > 0 && total > unread && role is not MailboxRole.Inbox and not MailboxRole.None)
        {
            text = string.Create(CultureInfo.InvariantCulture, $"{unread} / {total}");
        }
        else if (unread > 0)
        {
            text = unread.ToString(CultureInfo.InvariantCulture);
        }
        else if (total > 0 && role is MailboxRole.Drafts or MailboxRole.Sent or MailboxRole.Junk or MailboxRole.None)
        {
            text = total.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            text = string.Empty;
        }

        // Settings > Accessibility > Unread counts: a number, a dot, or nothing
        if (!string.Equals(unreadStyle, "count", StringComparison.Ordinal) && unread > 0)
        {
            return string.Equals(unreadStyle, "dot", StringComparison.Ordinal) ? "●" : string.Empty;
        }

        return text;
    }

    private static void Walk(
        string parent,
        int depth,
        Dictionary<string, List<Mailbox>> children,
        ISet<string> collapsed,
        List<FolderRowViewModel> output,
        FolderRowOptions options)
    {
        if (!children.TryGetValue(parent, out var kids))
        {
            return;
        }

        foreach (var m in kids)
        {
            var hasKids = children.ContainsKey(m.Id);
            var closed = collapsed.Contains(m.Id);
            var color = options.Colorful ? RoleColor(m.Role) : 0xa3a3a3;
            output.Add(new FolderRowViewModel(
                m.Id,
                DisplayName(m),
                m.Role,
                RoleGlyph(m.Role),
                depth,
                m.UnreadEmails,
                m.TotalEmails,
                options.Cvd ? A11y.CvdColor(color) : color,
                CountText(m.Role, m.UnreadEmails, m.TotalEmails, options.ShowTotals, options.UnreadStyle),
                hasKids ? (closed ? 2 : 1) : 0,
                !m.IsSubscribed,
                hasKids && closed && UnreadBelow(m.Id, children))
            {
                RoleTint = options.Colorful ? BulwarkTint(m.Role, options.Cvd) : null,
                RoleCvdRgb = options.Colorful && options.Cvd ? BulwarkCvdColor(m.Role) : 0,
            });
            if (!closed)
            {
                Walk(m.Id, depth + 1, children, collapsed, output, options);
            }
        }
    }

    private static bool UnreadBelow(string id, Dictionary<string, List<Mailbox>> children) =>
        children.TryGetValue(id, out var kids) && kids.Any(k => k.UnreadEmails > 0 || UnreadBelow(k.Id, children));

    private static int Rank(MailboxRole role) => role switch
    {
        MailboxRole.Inbox => 0,
        MailboxRole.Drafts => 1,
        MailboxRole.Sent => 2,
        MailboxRole.Archive => 3,
        MailboxRole.Junk => 4,
        MailboxRole.Trash => 5,
        _ => 10,
    };

    private static string RoleGlyph(MailboxRole role) => role switch
    {
        MailboxRole.Inbox => "",
        MailboxRole.Drafts => "",
        MailboxRole.Sent => "",
        MailboxRole.Archive => "",
        MailboxRole.Junk => "",
        MailboxRole.Trash => "",
        _ => "",
    };

    /// <summary>The palette key of a role's icon in the Bulwark style (sidebar.tsx:263-275): the chart tokens, muted for Trash; null for the roles without one (they take the text colour).</summary>
    private static string? BulwarkTint(MailboxRole role, bool cvd) => role switch
    {
        MailboxRole.Trash => "Dim",
        _ when cvd => null,
        MailboxRole.Inbox => "Chart1",
        MailboxRole.Sent => "Chart2",
        MailboxRole.Drafts => "Chart5",
        MailboxRole.Junk => "Chart4",
        MailboxRole.Archive => "Chart3",
        _ => null,
    };

    /// <summary>The colour-blind-safe colour of a role's icon (sidebar.tsx:264-269); 0 for a role without one.</summary>
    private static uint BulwarkCvdColor(MailboxRole role) => role switch
    {
        MailboxRole.Inbox => 0x0072B2,
        MailboxRole.Sent => 0x009E73,
        MailboxRole.Drafts => 0xCC79A7,
        MailboxRole.Junk => 0xD55E00,
        MailboxRole.Archive => 0x56B4E9,
        _ => 0,
    };

    private static uint RoleColor(MailboxRole role) => role switch
    {
        MailboxRole.Inbox => 0x818cf8,
        MailboxRole.Drafts => 0xa78bfa,
        MailboxRole.Sent => 0x34d399,
        MailboxRole.Archive => 0xf59e0b,
        MailboxRole.Junk => 0xf87171,
        MailboxRole.Trash => 0xa1a1aa,
        _ => 0xd4d4d4,
    };
}
