// Ported from Rust src/shortcuts.rs tests (:697-:841). Implemented by stream menus: ShortcutTables (M2 plan 6.2, T-260..T-270).
// Key texts: ShortcutKeys carries the Slint code points the Rust tests used (see its header).

using Barbican.Presentation.Input;

namespace Barbican.Presentation.Tests.Input;

public sealed class ShortcutTablesTests
{
    private static bool Eq(string actual, string expected) => string.Equals(actual, expected, StringComparison.Ordinal);

    private static ShortcutContext Ctx() => new() { SingleKeys = true, HasMail = true };

    private static ShortcutAction R(ShortcutPlace place, ShortcutContext c, string t, bool shift, bool ctrl, bool alt) =>
        ShortcutTables.Resolve(place, c, t, shift, ctrl, alt);

    [Fact]
    [RustTest("shortcuts::mail_keys_never_flip_and_backspace_does_not_delete")]
    public void Mail_keys_never_flip_and_backspace_does_not_delete()
    {
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Mail, Ctx(), ShortcutKeys.Backspace, false, false, false));
        Assert.Equal("read!", ShortcutTables.MailKeyAction("I"));
        Assert.Equal("unread!", ShortcutTables.MailKeyAction("u"));

        // every single-key hint the palette shows for mail resolves to that command
        foreach (var (k, a) in new[] { ("r", "reply"), ("a", "reply-all"), ("f", "forward"), ("e", "archive"), ("#", "delete"), ("s", "star"), ("!", "spam") })
        {
            var resolved = R(ShortcutPlace.Mail, Ctx(), k, false, false, false);
            string? viaTable = resolved.Kind == ShortcutKind.Mail ? resolved.Name : ShortcutTables.MailKeyAction(k);
            Assert.Equal(a, viaTable);
        }
    }

    [Fact]
    [RustTest("shortcuts::palette_and_help_everywhere")]
    public void Palette_and_help_everywhere()
    {
        foreach (var p in new[] { ShortcutPlace.Mail, ShortcutPlace.Calendar, ShortcutPlace.Contacts, ShortcutPlace.Files, ShortcutPlace.Settings })
        {
            Assert.Equal(ShortcutAction.Palette, R(p, Ctx(), "k", false, true, false));
            Assert.Equal(ShortcutAction.Help, R(p, Ctx(), "?", true, false, false));
            Assert.Equal(ShortcutAction.Refresh, R(p, Ctx(), ShortcutKeys.F5, false, false, false));
            Assert.Equal(ShortcutAction.A11y, R(p, Ctx(), "A", true, false, true));
        }

        // the composer keeps Ctrl+K for links and types "?"
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Composer, Ctx(), "k", false, true, false));
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Composer, Ctx(), "?", true, false, false));
    }

    [Fact]
    [RustTest("shortcuts::single_keys_off_leaves_named_keys")]
    public void Single_keys_off_leaves_named_keys()
    {
        var c = Ctx() with { SingleKeys = false };
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Mail, c, "?", true, false, false));
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Mail, c, "x", false, false, false));
        Assert.Equal(ShortcutAction.Open, R(ShortcutPlace.Mail, c, "\n", false, false, false));
        Assert.Equal(ShortcutAction.Palette, R(ShortcutPlace.Mail, c, "k", false, true, false));
        Assert.Equal(ShortcutAction.Cal("prev"), R(ShortcutPlace.Calendar, c, ShortcutKeys.Left, false, false, false));
    }

    [Fact]
    [RustTest("shortcuts::g_sequences")]
    public void G_sequences()
    {
        Assert.Equal(ShortcutAction.ArmGo, R(ShortcutPlace.Mail, Ctx(), "g", false, false, false));
        var armed = Ctx() with { GPending = true };
        Assert.Equal(ShortcutAction.GoTo("inbox"), R(ShortcutPlace.Mail, armed, "i", false, false, false));
        Assert.Equal(ShortcutAction.GoTo("starred"), R(ShortcutPlace.Mail, armed, "s", false, false, false));
        Assert.Equal(ShortcutAction.GoTo("sent"), R(ShortcutPlace.Mail, armed, "t", false, false, false));
        Assert.Equal(ShortcutAction.GoTo("drafts"), R(ShortcutPlace.Mail, armed, "d", false, false, false));
        Assert.Equal(ShortcutAction.GoTo("all"), R(ShortcutPlace.Mail, armed, "a", false, false, false));

        // without the g, s and a are star and reply-all again
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Mail, Ctx(), "s", false, false, false));
        Assert.Equal(ShortcutAction.MailAction("reply-all"), R(ShortcutPlace.Mail, Ctx(), "a", false, false, false));

        // Shift+G stays refresh (older handler)
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Mail, armed, "G", true, false, false));
    }

    [Fact]
    [RustTest("shortcuts::mail_keys")]
    public void Mail_keys()
    {
        Assert.Equal(ShortcutAction.Extend(1), R(ShortcutPlace.Mail, Ctx(), ShortcutKeys.Down, true, false, false));
        Assert.Equal(ShortcutAction.Extend(-1), R(ShortcutPlace.Mail, Ctx(), "K", true, false, false));
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Mail, Ctx(), ShortcutKeys.Down, false, false, false));
        Assert.Equal(ShortcutAction.Open, R(ShortcutPlace.Mail, Ctx(), "o", false, false, false));
        Assert.Equal(ShortcutAction.ToggleSelect, R(ShortcutPlace.Mail, Ctx(), "x", false, false, false));
        Assert.Equal(ShortcutAction.MailAction("forward"), R(ShortcutPlace.Mail, Ctx(), "f", false, true, false));
        var none = Ctx() with { HasMail = false };
        Assert.Equal(ShortcutAction.FocusSearch, R(ShortcutPlace.Mail, none, "f", false, true, false));
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Mail, none, "x", false, false, false));
        Assert.Equal(ShortcutAction.MailAction("read!"), R(ShortcutPlace.Mail, Ctx(), "q", false, true, false));
        Assert.Equal(ShortcutAction.Undo, R(ShortcutPlace.Mail, Ctx(), "z", false, true, false));

        // M2 hotfix: Bulwark use-undo-shortcut.ts isUndoKey, `z` alone while single keys are on; never Shift+Z, never with single keys off
        Assert.Equal(ShortcutAction.Undo, R(ShortcutPlace.Mail, Ctx(), "z", false, false, false));
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Mail, Ctx(), "Z", true, false, false));
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Mail, Ctx() with { SingleKeys = false }, "z", false, false, false));
        Assert.Equal(ShortcutAction.Refresh, R(ShortcutPlace.Mail, Ctx(), ShortcutKeys.F9, false, false, false));
        Assert.Equal(ShortcutAction.Compose, R(ShortcutPlace.Mail, Ctx(), "M", true, true, false));
    }

    [Fact]
    [RustTest("shortcuts::composer_keys")]
    public void Composer_keys()
    {
        Assert.Equal(ShortcutAction.ShowCc, R(ShortcutPlace.Composer, Ctx(), "C", true, true, false));
        Assert.Equal(ShortcutAction.ShowBcc, R(ShortcutPlace.Composer, Ctx(), "B", true, true, false));
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Composer, Ctx(), "b", false, true, false));
    }

    [Fact]
    [RustTest("shortcuts::calendar_keys")]
    public void Calendar_keys()
    {
        var c = Ctx();
        Assert.Equal(ShortcutAction.Cal("next"), R(ShortcutPlace.Calendar, c, ShortcutKeys.Right, false, false, false));
        Assert.Equal(ShortcutAction.Cal("today"), R(ShortcutPlace.Calendar, c, "t", false, false, false));
        Assert.Equal(ShortcutAction.CalView(1), R(ShortcutPlace.Calendar, c, "w", false, false, false));
        Assert.Equal(ShortcutAction.NewEvent, R(ShortcutPlace.Calendar, c, "n", false, false, false));
        Assert.Equal(ShortcutAction.NewEvent, R(ShortcutPlace.Calendar, c, "c", false, false, false));
        Assert.Equal(ShortcutAction.FocusSearch, R(ShortcutPlace.Calendar, c, "/", false, false, false));
        Assert.Equal(ShortcutAction.Refresh, R(ShortcutPlace.Calendar, c, "r", false, true, false));
        var open = c with { EditorOpen = true };
        Assert.Equal(ShortcutAction.SaveEvent, R(ShortcutPlace.Calendar, open, "\n", false, true, false));
        Assert.Equal(ShortcutAction.CloseEditor, R(ShortcutPlace.Calendar, open, ShortcutKeys.Escape, false, false, false));
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Calendar, open, "t", false, false, false));
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Calendar, open, "?", true, false, false));
    }

    [Fact]
    [RustTest("shortcuts::list_keys")]
    public void List_keys()
    {
        var c = Ctx() with { HasContact = true, HasFile = true };
        Assert.Equal(ShortcutAction.Move(1), R(ShortcutPlace.Contacts, c, ShortcutKeys.Down, false, false, false));
        Assert.Equal(ShortcutAction.First, R(ShortcutPlace.Contacts, c, ShortcutKeys.Home, false, false, false));
        Assert.Equal(ShortcutAction.Delete, R(ShortcutPlace.Contacts, c, ShortcutKeys.Delete, false, false, false));
        Assert.Equal(ShortcutAction.Menu, R(ShortcutPlace.Contacts, c, ShortcutKeys.F10, true, false, false));
        Assert.Equal(ShortcutAction.Up, R(ShortcutPlace.Files, c, ShortcutKeys.Up, false, false, true));
        Assert.Equal(ShortcutAction.Rename, R(ShortcutPlace.Files, c, ShortcutKeys.F2, false, false, false));
        Assert.Equal(ShortcutAction.Cut, R(ShortcutPlace.Files, c, "x", false, true, false));
        Assert.Equal(ShortcutAction.Paste, R(ShortcutPlace.Files, c, "v", false, true, false));
        var none = Ctx() with { HasFile = false, HasContact = false };
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Files, none, ShortcutKeys.Delete, false, false, false));
        Assert.Equal(ShortcutAction.Pass, R(ShortcutPlace.Contacts, none, "\n", false, false, false));
    }

    [Fact]
    [RustTest("shortcuts::stepping_through_a_list")]
    public void Stepping_through_a_list()
    {
        string[] ids = ["a", "b", "c"];
        Assert.Equal(0, ShortcutTables.Step(ids, string.Empty, ShortcutAction.Move(1)));
        Assert.Equal(2, ShortcutTables.Step(ids, string.Empty, ShortcutAction.Move(-1)));
        Assert.Equal(2, ShortcutTables.Step(ids, "b", ShortcutAction.Move(1)));
        Assert.Equal(2, ShortcutTables.Step(ids, "c", ShortcutAction.Move(1)));
        Assert.Equal(0, ShortcutTables.Step(ids, "a", ShortcutAction.Move(-1)));
        Assert.Equal(2, ShortcutTables.Step(ids, "b", ShortcutAction.Last));
        Assert.Null(ShortcutTables.Step([], "b", ShortcutAction.Last));
    }

    [Fact]
    [RustTest("shortcuts::help_rows_split_keys")]
    public void Help_rows_split_keys()
    {
        Assert.Equal(["j", "↓"], ShortcutTables.Alternatives("j / ↓"));
        Assert.Equal(["/"], ShortcutTables.Alternatives("/"));
        Assert.Equal(["Ctrl + E", "/"], ShortcutTables.Alternatives("Ctrl + E / /"));
        Assert.Equal(["Ctrl + 1", "Ctrl + 2"], ShortcutTables.Alternatives("Ctrl + 1 / Ctrl + 2"));

        var rows = ShortcutTables.HelpRows(0);
        Assert.Equal("Navigation", rows[0].Section);
        Assert.Contains(rows, r => Eq(r.Label, "Show shortcuts") && r.Keys.SequenceEqual(["?"], StringComparer.Ordinal));

        // apps without a Global section get one with the general keys
        var files = ShortcutTables.HelpRows(3);
        Assert.Contains(files, r => Eq(r.Section, "Global"));
        Assert.Contains(files, r => Eq(r.Label, "Open the command palette"));

        // general keys are listed once per tab
        Assert.Single(ShortcutTables.HelpRows(1), r => Eq(r.Label, "Show shortcuts"));
    }

    [Fact]
    [RustTest("shortcuts::single_key_detection")]
    public void Single_key_detection()
    {
        Assert.True(ShortcutTables.IsSingleKey("x"));
        Assert.True(ShortcutTables.IsSingleKey("?"));
        Assert.False(ShortcutTables.IsSingleKey("\n"));
        Assert.False(ShortcutTables.IsSingleKey(ShortcutKeys.Down));
        Assert.False(ShortcutTables.IsSingleKey("Ctrl + K"));
    }
}
