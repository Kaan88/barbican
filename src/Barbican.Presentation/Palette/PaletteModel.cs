// Mirrors: Rust palette.rs:61 command_matches, :92 commands, :178 filter; Bulwark lib/command-registry.ts:53-96 commandMatches and the command-palette
// ordering (the open app's own, Go to, Actions, Account). Stub from stage F of M2; owner menus.

using Barbican.Presentation.Input;

namespace Barbican.Presentation.Palette;

/// <summary>The palette's pure model: which commands exist for a context and which match what was typed.</summary>
public static class PaletteModel
{
    /// <summary>Every word of the needle is in the label or the keywords (case-insensitive); an empty needle matches everything.</summary>
    public static bool CommandMatches(string needle, string label, IReadOnlyList<string> keywords)
    {
        var words = needle.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return true;
        }

        var hay = string.Join(' ', keywords.Prepend(label)).ToLowerInvariant();
        return words.All(w => hay.Contains(w.ToLowerInvariant(), StringComparison.Ordinal));
    }

    // MA0051: a faithful port of one list (Rust palette.rs:92 commands); the order of its rows is the behaviour.
#pragma warning disable MA0051
    /// <summary>The commands for a context in Bulwark's order: the open app's own, Go to, Actions, Account (Rust palette.rs:92).</summary>
    public static IReadOnlyList<PaletteCommand> Commands(PaletteContext x)
    {
        var v = new List<PaletteCommand>();
        // single-key hints only while single-key shortcuts are on
        string K(string s) => x.SingleKeys || !ShortcutTables.IsSingleKey(s) ? s : string.Empty;
        static PaletteCommand Cmd(string id, string group, string icon, string label, string shortcut, params string[] keywords) =>
            new(id, group, icon, label, keywords, shortcut);

        if (x.SettingsOpen)
        {
            foreach (var (id, label) in x.SettingsTabs)
            {
                if (string.Equals(id, x.SettingsTab, StringComparison.Ordinal))
                {
                    continue;
                }

                v.Add(Cmd("settings:" + id, "Settings", "settings", "Go to " + label + " settings", string.Empty, "settings"));
            }
        }
        else
        {
            switch (x.App)
            {
                case 0 when x.HasMail:
                    const string g = "Mail";
                    v.Add(Cmd("mail:reply", g, "reply", "Reply", K("r")));
                    v.Add(Cmd("mail:reply-all", g, "reply-all", "Reply all", K("a")));
                    v.Add(Cmd("mail:forward", g, "forward", "Forward", K("f")));
                    v.Add(Cmd("mail:archive", g, "archive", "Archive", K("e")));
                    v.Add(Cmd("mail:delete", g, "delete", "Delete", K("#")));
                    v.Add(x.MailUnread
                        ? Cmd("mail:read", g, "read", "Mark as read", "Shift + I")
                        : Cmd("mail:unread", g, "mail", "Mark as unread", K("u")));
                    v.Add(Cmd("mail:star", g, "star", x.MailFlagged ? "Unstar" : "Star", K("s")));
                    v.Add(Cmd("mail:spam", g, "spam", x.InJunk ? "Not spam" : "Mark as spam", K("!")));
                    break;
                case 1:
                    const string c = "Calendar";
                    v.Add(Cmd("cal:new", c, "add", "New event", K("n")));
                    v.Add(Cmd("cal:search", c, "search", "Search events", K("/")));
                    v.Add(Cmd("cal:today", c, "today", "Go to today", K("t")));
                    (string Key, string Label)[] views = [("d", "Day view"), ("w", "Week view"), ("m", "Month view")];
                    for (var i = 0; i < views.Length; i++)
                    {
                        if (x.CalView != i)
                        {
                            v.Add(Cmd("cal:view" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), c, "calendar", views[i].Label, K(views[i].Key)));
                        }
                    }

                    v.Add(Cmd("cal:refresh", c, "refresh", "Refresh", "F5"));
                    break;
                case 2:
                    v.Add(Cmd("ct:new", "Contacts", "add", "New contact", string.Empty));
                    v.Add(Cmd("ct:group", "Contacts", "people", "New group", string.Empty));
                    break;
                case 3:
                    const string f = "Files";
                    if (!x.InTrash)
                    {
                        v.Add(Cmd("fl:upload", f, "upload", "Upload files", string.Empty));
                        v.Add(Cmd("fl:upload-folder", f, "upload", "Upload folder", string.Empty));
                        v.Add(Cmd("fl:new-folder", f, "folder", "New folder", string.Empty));
                        v.Add(Cmd("fl:trash", f, "delete", "Trash", string.Empty));
                    }
                    else
                    {
                        v.Add(Cmd("fl:empty-trash", f, "delete", "Empty trash", string.Empty));
                    }

                    v.Add(Cmd("fl:refresh", f, "refresh", "Refresh", string.Empty));
                    break;
                default:
                    break;
            }
        }

        const string nav = "Go to";
        v.Add(Cmd("go:mail", nav, "mail", "Go to Mail", string.Empty));
        if (x.Calendars)
        {
            v.Add(Cmd("go:calendar", nav, "calendar", "Go to Calendar", string.Empty));
        }

        if (x.Contacts)
        {
            v.Add(Cmd("go:contacts", nav, "people", "Go to Contacts", string.Empty));
        }

        if (x.Files)
        {
            v.Add(Cmd("go:files", nav, "folder", "Go to Files", string.Empty));
        }

        v.Add(Cmd("go:settings", nav, "settings", "Go to Settings", string.Empty));
        if (x.Admin)
        {
            v.Add(Cmd("go:admin", nav, "admin", "Go to Stalwart admin", string.Empty));
        }

        // "c" and "/" compose and search only in Mail (another app gives the same keys its own meaning)
        var inMail = x.App == 0 && !x.SettingsOpen;
        const string act = "Actions";
        v.Add(Cmd("compose", act, "compose", "Compose", inMail ? K("c") : string.Empty));
        v.Add(Cmd("search", act, "search", "Search mail", inMail ? K("/") : string.Empty));
        v.Add(Cmd("search-everything", act, "search", "Search everything", string.Empty, "Contacts", "Calendar", "Files"));
        v.Add(Cmd("theme", act, x.Dark ? "sun" : "moon", x.Dark ? "Switch to light theme" : "Switch to dark theme", string.Empty));
        v.Add(Cmd("shortcuts", act, "keys", "Show keyboard shortcuts", K("?")));
        v.Add(Cmd("a11y", "Accessibility", "a11y", "Open the accessibility options", "Alt + Shift + A", "contrast", "font", "motion", "focus"));
        foreach (var a in x.Accounts)
        {
            if (string.Equals(a, x.Account, StringComparison.Ordinal))
            {
                continue;
            }

            v.Add(Cmd("account:" + a, "Account", "user", "Switch to " + a, string.Empty, "Switch account", a));
        }

        v.Add(Cmd("sign-out", "Account", "sign-out", x.Account.Length == 0 ? "Sign out" : "Sign out of " + x.Account, string.Empty));
        return v;
    }
#pragma warning restore MA0051

    /// <summary>The commands whose label or keywords match the needle, in order.</summary>
    public static IReadOnlyList<PaletteCommand> Filter(IReadOnlyList<PaletteCommand> commands, string needle) =>
        commands.Where(c => CommandMatches(needle, c.Label, c.Keywords)).ToList();
}
