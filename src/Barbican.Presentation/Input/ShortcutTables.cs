// Mirrors: Rust shortcuts.rs:105 is_single_key, :108 resolve, :138 mail, :172 mail_key_action, :370 alternatives, :384 help_rows, :602 step;
// Bulwark lib/keyboard-shortcuts/{index,mail,calendar,contacts,files,settings,types}.ts, hooks/use-keyboard-shortcuts.ts, lib/keyboard.ts:96-101
// (the typed character wins, R2-11). Owner menus. The M1 KeyRouter keeps working beside this table.

namespace Barbican.Presentation.Input;

/// <summary>The shortcut tables as pure functions over key texts.</summary>
public static class ShortcutTables
{
    private sealed record Entry(string Keys, string Label);

    private sealed record Section(string Name, Entry[] Entries);

    // Rust shortcuts.rs:282 MAIL_HELP .. :367 GENERAL_HELP (Bulwark's sections and words).
    private static readonly Section[] MailHelp =
    [
        new("Navigation",
        [
            new("j / ↓", "Next email"),
            new("k / ↑", "Previous email"),
            new("Enter / o", "Open email"),
            new("Esc", "Close / Deselect"),
            new("n", "Next unread email"),
            new("g → i / s / t / d / a", "Go to folder"),
        ]),
        new("Email Actions",
        [
            new("Ctrl + R / r", "Reply"),
            new("Ctrl + Shift + R / Shift + R", "Reply all"),
            new("Ctrl + F / Ctrl + Shift + F / f", "Forward"),
            new("s", "Toggle star"),
            new("e", "Archive"),
            new("Del", "Delete"),
            new("Ctrl + U / u", "Mark as unread"),
            new("Ctrl + Q / Shift + I", "Mark as read"),
            new("!", "Report spam / Not spam"),
            new("Ctrl + Z / z", "Undo the last action"),
        ]),
        new("Global",
        [
            new("Ctrl + N / Ctrl + Shift + M / c", "Compose new email"),
            new("Ctrl + E / /", "Focus search"),
            new("F9 / F5 / Shift + G", "Refresh emails"),
            new("Ctrl + 1 / Ctrl + 2 / Ctrl + 3 / Ctrl + 4", "Go to Mail, Calendar, Contacts or Files"),
            new("Ctrl + A", "Select all"),
            new("Shift + ↓ / Shift + ↑", "Extend selection down / up"),
        ]),
        new("Threads", [new("x", "Select the conversation")]),
        new("Composer",
        [
            new("Ctrl + Enter", "Send email"),
            new("Ctrl + S", "Save draft"),
            new("Ctrl + K", "Insert link"),
            new("Ctrl + Shift + C", "Show Cc"),
            new("Ctrl + Shift + B", "Show Bcc"),
        ]),
    ];

    private static readonly Section[] CalendarHelp =
    [
        new("Navigation",
        [
            new("←", "Previous day, week or month"),
            new("→", "Next day, week or month"),
            new("t", "Go to today"),
        ]),
        new("Views", [new("d", "Day view"), new("w", "Week view"), new("m", "Month view")]),
        new("Actions",
        [
            new("/", "Search events"),
            new("n / c", "New event"),
            new("e", "Edit event"),
            new("Ctrl + Enter", "Save"),
            new("Esc", "Close"),
        ]),
        new("Global", [new("F5 / Ctrl + R", "Refresh")]),
    ];

    private static readonly Section[] ContactsHelp =
    [
        new("List",
        [
            new("↓ / ↑", "Next or previous contact"),
            new("Home / End", "First or last"),
            new("Enter", "Open the contact"),
            new("Esc", "Clear the selection or search"),
        ]),
        new("Actions", [new("Del / Backspace", "Delete"), new("Shift + F10", "Open the contact's menu")]),
        new("Global", [new("/", "Search")]),
    ];

    private static readonly Section[] FilesHelp =
    [
        new("List",
        [
            new("↓ / ↑", "Next or previous item"),
            new("Home / End", "First or last"),
            new("Enter", "Open"),
            new("Esc", "Clear the selection or search"),
            new("Alt + ↑", "Up a folder"),
        ]),
        new("Actions",
        [
            new("F2", "Rename"),
            new("Del / Backspace", "Delete"),
            new("Ctrl + C", "Copy"),
            new("Ctrl + X", "Cut"),
            new("Ctrl + V", "Paste"),
            new("Shift + F10", "Open the item's menu"),
        ]),
    ];

    // Bulwark GENERAL_SHORTCUTS, added to each app's "Global" section.
    private static readonly Entry[] GeneralHelp =
    [
        new("Ctrl + K", "Open the command palette"),
        new("?", "Show shortcuts"),
        new("Alt + Shift + A", "Open the accessibility options"),
    ];

    /// <summary>The help sheet's tabs.</summary>
    public static IReadOnlyList<string> Tabs { get; } = ["Mail", "Calendar", "Contacts", "Files"];

    /// <summary>A printable ASCII key that only counts as a single-key shortcut (WCAG 2.1.4), which Settings can turn off.</summary>
    public static bool IsSingleKey(string text) => text.Length == 1 && text[0] is > ' ' and <= '~';

    private static bool IsEnter(string t) => t is "\n" or "\r";

    /// <summary>Turns a key press into an action; <paramref name="text"/> already has shift applied.</summary>
    public static ShortcutAction Resolve(ShortcutPlace place, ShortcutContext context, string text, bool shift, bool ctrl, bool alt)
    {
        var l = text.ToLowerInvariant();

        // composer (Bulwark email-composer.tsx capture handler)
        if (place == ShortcutPlace.Composer)
        {
            if (ctrl && shift && !alt && string.Equals(l, "c", StringComparison.Ordinal))
            {
                return ShortcutAction.ShowCc;
            }

            return ctrl && shift && !alt && string.Equals(l, "b", StringComparison.Ordinal) ? ShortcutAction.ShowBcc : ShortcutAction.Pass;
        }

        // every app
        if (ctrl && !shift && !alt && string.Equals(l, "k", StringComparison.Ordinal))
        {
            return ShortcutAction.Palette;
        }

        if (alt && shift && !ctrl && string.Equals(l, "a", StringComparison.Ordinal))
        {
            return ShortcutAction.A11y;
        }

        if (!ctrl && !alt && (string.Equals(text, ShortcutKeys.F5, StringComparison.Ordinal) || string.Equals(text, ShortcutKeys.F9, StringComparison.Ordinal)))
        {
            return ShortcutAction.Refresh;
        }

        if (ctrl && shift && !alt && string.Equals(l, "m", StringComparison.Ordinal))
        {
            return ShortcutAction.Compose;
        }

        var single = IsSingleKey(text) && !ctrl && !alt;
        if (single && !context.SingleKeys)
        {
            return ShortcutAction.Pass;
        }

        if (single && string.Equals(text, "?", StringComparison.Ordinal) && !(place == ShortcutPlace.Calendar && context.EditorOpen))
        {
            return ShortcutAction.Help;
        }

        return place switch
        {
            ShortcutPlace.Mail => Mail(context, text, l, shift, ctrl, alt),
            ShortcutPlace.Calendar => Calendar(context, text, l, ctrl, alt),
            ShortcutPlace.Contacts => Contacts(context, text, shift, ctrl, alt),
            ShortcutPlace.Files => Files(context, text, l, shift, ctrl, alt),
            _ => ShortcutAction.Pass,
        };
    }

    private static ShortcutAction Mail(ShortcutContext ctx, string t, string l, bool shift, bool ctrl, bool alt)
    {
        if (alt)
        {
            return ShortcutAction.Pass;
        }

        if (ctrl)
        {
            // Bulwark desktopPcKey: Ctrl+F forwards with a message selected (Outlook) and searches elsewhere
            return (l, shift) switch
            {
                ("f", false) => ctx.HasMail ? ShortcutAction.MailAction("forward") : ShortcutAction.FocusSearch,
                ("f", true) => ctx.HasMail ? ShortcutAction.MailAction("forward") : ShortcutAction.Swallow,
                ("e", false) => ShortcutAction.FocusSearch,
                ("q", false) => ShortcutAction.MailAction("read!"),
                ("z", false) => ShortcutAction.Undo,
                _ => ShortcutAction.Pass,
            };
        }

        // second half of a g sequence, before the single keys so s / t / a / d jump
        if (ctx.GPending && !shift)
        {
            var target = l switch { "i" => "inbox", "s" => "starred", "t" => "sent", "d" => "drafts", "a" => "all", _ => null };
            if (target is not null)
            {
                return ShortcutAction.GoTo(target);
            }
        }

        if (shift && (string.Equals(t, ShortcutKeys.Down, StringComparison.Ordinal) || string.Equals(t, "J", StringComparison.Ordinal)))
        {
            return ShortcutAction.Extend(1);
        }

        if (shift && (string.Equals(t, ShortcutKeys.Up, StringComparison.Ordinal) || string.Equals(t, "K", StringComparison.Ordinal)))
        {
            return ShortcutAction.Extend(-1);
        }

        if (IsEnter(t) || string.Equals(t, "o", StringComparison.Ordinal))
        {
            return ShortcutAction.Open;
        }

        return t switch
        {
            "g" => ShortcutAction.ArmGo,
            // Bulwark hooks/use-undo-shortcut.ts:15-25 isUndoKey: `z` alone undoes while single-key shortcuts are on (the help sheet
            // always said "Ctrl + Z / z"; M2 hotfix: only Ctrl+Z was mapped, HAND TEST 2)
            "z" => ShortcutAction.Undo,
            "x" => ctx.HasMail ? ShortcutAction.ToggleSelect : ShortcutAction.Pass,
            // Bulwark's unadvertised alias of Shift+R
            "a" => ctx.HasMail ? ShortcutAction.MailAction("reply-all") : ShortcutAction.Pass,
            _ => ShortcutAction.Pass,
        };
    }

    /// <summary>The mail command a single key stands for outside the table ("I" read, "u" unread, ...), or null.</summary>
    public static string? MailKeyAction(string text) => text switch
    {
        "r" => "reply",
        "R" or "a" => "reply-all",
        "f" => "forward",
        "s" => "star",
        "e" => "archive",
        "#" or ShortcutKeys.Delete => "delete",
        "u" => "unread!",
        "I" => "read!",
        "!" => "spam",
        _ => null,
    };

    private static ShortcutAction Calendar(ShortcutContext ctx, string t, string l, bool ctrl, bool alt)
    {
        if (ctx.EditorOpen)
        {
            // the event editor (event-modal.tsx): Mod+Enter saves, Esc closes
            if (ctrl && IsEnter(t))
            {
                return ShortcutAction.SaveEvent;
            }

            return string.Equals(t, ShortcutKeys.Escape, StringComparison.Ordinal) ? ShortcutAction.CloseEditor : ShortcutAction.Pass;
        }

        if (ctrl && !alt && string.Equals(l, "r", StringComparison.Ordinal))
        {
            return ShortcutAction.Refresh;
        }

        if (ctrl || alt)
        {
            return ShortcutAction.Pass;
        }

        if (string.Equals(t, ShortcutKeys.Left, StringComparison.Ordinal))
        {
            return ShortcutAction.Cal("prev");
        }

        if (string.Equals(t, ShortcutKeys.Right, StringComparison.Ordinal))
        {
            return ShortcutAction.Cal("next");
        }

        return t switch
        {
            "t" => ShortcutAction.Cal("today"),
            "d" => ShortcutAction.CalView(0),
            "w" => ShortcutAction.CalView(1),
            "m" => ShortcutAction.CalView(2),
            "n" or "c" => ShortcutAction.NewEvent,
            "e" => ShortcutAction.EditEvent,
            "/" => ShortcutAction.FocusSearch,
            _ => ShortcutAction.Pass,
        };
    }

    private static ShortcutAction Contacts(ShortcutContext ctx, string t, bool shift, bool ctrl, bool alt)
    {
        if (ctrl || alt)
        {
            return ShortcutAction.Pass;
        }

        if (string.Equals(t, ShortcutKeys.Down, StringComparison.Ordinal))
        {
            return ShortcutAction.Move(1);
        }

        if (string.Equals(t, ShortcutKeys.Up, StringComparison.Ordinal))
        {
            return ShortcutAction.Move(-1);
        }

        if (string.Equals(t, ShortcutKeys.Home, StringComparison.Ordinal))
        {
            return ShortcutAction.First;
        }

        if (string.Equals(t, ShortcutKeys.End, StringComparison.Ordinal))
        {
            return ShortcutAction.Last;
        }

        if (IsEnter(t))
        {
            return ctx.HasContact ? ShortcutAction.OpenItem : ShortcutAction.Pass;
        }

        if (string.Equals(t, ShortcutKeys.Delete, StringComparison.Ordinal) || string.Equals(t, ShortcutKeys.Backspace, StringComparison.Ordinal))
        {
            return ctx.HasContact ? ShortcutAction.Delete : ShortcutAction.Pass;
        }

        if (string.Equals(t, ShortcutKeys.Escape, StringComparison.Ordinal))
        {
            return ShortcutAction.Clear;
        }

        if (string.Equals(t, ShortcutKeys.F10, StringComparison.Ordinal) && shift)
        {
            return ctx.HasContact ? ShortcutAction.Menu : ShortcutAction.Pass;
        }

        return string.Equals(t, "/", StringComparison.Ordinal) ? ShortcutAction.FocusSearch : ShortcutAction.Pass;
    }

    // MA0051: a faithful port of one key table (Rust shortcuts.rs:228 files); splitting it would scatter the order of its rules.
#pragma warning disable MA0051
    private static ShortcutAction Files(ShortcutContext ctx, string t, string l, bool shift, bool ctrl, bool alt)
    {
        if (alt && !ctrl && string.Equals(t, ShortcutKeys.Up, StringComparison.Ordinal))
        {
            return ShortcutAction.Up;
        }

        if (alt)
        {
            return ShortcutAction.Pass;
        }

        if (ctrl && !shift)
        {
            return l switch
            {
                "c" => ctx.HasFile ? ShortcutAction.Copy : ShortcutAction.Pass,
                "x" => ctx.HasFile ? ShortcutAction.Cut : ShortcutAction.Pass,
                "v" => ShortcutAction.Paste,
                _ => ShortcutAction.Pass,
            };
        }

        if (ctrl)
        {
            return ShortcutAction.Pass;
        }

        if (string.Equals(t, ShortcutKeys.Down, StringComparison.Ordinal))
        {
            return ShortcutAction.Move(1);
        }

        if (string.Equals(t, ShortcutKeys.Up, StringComparison.Ordinal))
        {
            return ShortcutAction.Move(-1);
        }

        if (string.Equals(t, ShortcutKeys.Home, StringComparison.Ordinal))
        {
            return ShortcutAction.First;
        }

        if (string.Equals(t, ShortcutKeys.End, StringComparison.Ordinal))
        {
            return ShortcutAction.Last;
        }

        if (IsEnter(t))
        {
            return ctx.HasFile ? ShortcutAction.OpenItem : ShortcutAction.Pass;
        }

        if (string.Equals(t, ShortcutKeys.F2, StringComparison.Ordinal))
        {
            return ctx.HasFile ? ShortcutAction.Rename : ShortcutAction.Pass;
        }

        if (string.Equals(t, ShortcutKeys.Delete, StringComparison.Ordinal) || string.Equals(t, ShortcutKeys.Backspace, StringComparison.Ordinal))
        {
            return ctx.HasFile ? ShortcutAction.Delete : ShortcutAction.Pass;
        }

        if (string.Equals(t, ShortcutKeys.Escape, StringComparison.Ordinal))
        {
            return ShortcutAction.Clear;
        }

        return string.Equals(t, ShortcutKeys.F10, StringComparison.Ordinal) && shift && ctx.HasFile ? ShortcutAction.Menu : ShortcutAction.Pass;
    }
#pragma warning restore MA0051

    /// <summary>The index a Move/First/Last action lands on in a list of ids, or null for an empty list.</summary>
    public static int? Step(IReadOnlyList<string> ids, string current, ShortcutAction action)
    {
        if (ids.Count == 0)
        {
            return null;
        }

        var last = ids.Count - 1;
        var at = -1;
        for (var i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], current, StringComparison.Ordinal))
            {
                at = i;
                break;
            }
        }

        switch (action.Kind)
        {
            case ShortcutKind.First:
                return 0;
            case ShortcutKind.Last:
                return last;
            case ShortcutKind.Move:
                if (at < 0)
                {
                    return action.Number > 0 ? 0 : last;
                }

                return Math.Clamp(at + action.Number, 0, last);
            default:
                return at < 0 ? null : at;
        }
    }

    /// <summary>Splits "j / down" style help text into its alternatives ("/" alone stays one key).</summary>
    public static IReadOnlyList<string> Alternatives(string keys)
    {
        if (string.Equals(keys.Trim(), "/", StringComparison.Ordinal))
        {
            return ["/"];
        }

        return keys.Split(" / ").Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    }

    /// <summary>The flattened rows of one help tab: a header row per section, then its entries; general keys listed once per tab.</summary>
    public static IReadOnlyList<HelpRow> HelpRows(int tab)
    {
        var sections = tab switch { 1 => CalendarHelp, 2 => ContactsHelp, 3 => FilesHelp, _ => MailHelp };
        var rows = new List<HelpRow>();
        var hadGlobal = false;
        foreach (var s in sections)
        {
            rows.Add(new HelpRow(s.Name, string.Empty, []));
            rows.AddRange(s.Entries.Select(e => new HelpRow(string.Empty, e.Label, Alternatives(e.Keys))));
            if (string.Equals(s.Name, "Global", StringComparison.Ordinal))
            {
                hadGlobal = true;
                rows.AddRange(GeneralHelp.Select(e => new HelpRow(string.Empty, e.Label, Alternatives(e.Keys))));
            }
        }

        if (!hadGlobal)
        {
            rows.Add(new HelpRow("Global", string.Empty, []));
            rows.AddRange(GeneralHelp.Select(e => new HelpRow(string.Empty, e.Label, Alternatives(e.Keys))));
        }

        return rows;
    }
}
