// Mirrors: Rust shortcuts.rs:58 Action with its payloads (GoTo(&str), Extend(i32), Mail(&str), Cal(&str), CalView(i32), Move(i32)). Stub from stage F of M2; owner menus.

namespace Barbican.Presentation.Input;

/// <summary>A resolved key press: a kind and at most one payload (a name or a number).</summary>
public readonly record struct ShortcutAction(ShortcutKind Kind, string? Name = null, int Number = 0)
{
    public static ShortcutAction Pass => new(ShortcutKind.Pass);

    public static ShortcutAction Swallow => new(ShortcutKind.Swallow);

    public static ShortcutAction Palette => new(ShortcutKind.Palette);

    public static ShortcutAction Help => new(ShortcutKind.Help);

    public static ShortcutAction A11y => new(ShortcutKind.A11y);

    public static ShortcutAction Refresh => new(ShortcutKind.Refresh);

    public static ShortcutAction Compose => new(ShortcutKind.Compose);

    public static ShortcutAction FocusSearch => new(ShortcutKind.FocusSearch);

    public static ShortcutAction ArmGo => new(ShortcutKind.ArmGo);

    public static ShortcutAction Open => new(ShortcutKind.Open);

    public static ShortcutAction ToggleSelect => new(ShortcutKind.ToggleSelect);

    public static ShortcutAction Undo => new(ShortcutKind.Undo);

    public static ShortcutAction ShowCc => new(ShortcutKind.ShowCc);

    public static ShortcutAction ShowBcc => new(ShortcutKind.ShowBcc);

    public static ShortcutAction NewEvent => new(ShortcutKind.NewEvent);

    public static ShortcutAction EditEvent => new(ShortcutKind.EditEvent);

    public static ShortcutAction SaveEvent => new(ShortcutKind.SaveEvent);

    public static ShortcutAction CloseEditor => new(ShortcutKind.CloseEditor);

    public static ShortcutAction First => new(ShortcutKind.First);

    public static ShortcutAction Last => new(ShortcutKind.Last);

    public static ShortcutAction OpenItem => new(ShortcutKind.OpenItem);

    public static ShortcutAction Delete => new(ShortcutKind.Delete);

    public static ShortcutAction Clear => new(ShortcutKind.Clear);

    public static ShortcutAction Menu => new(ShortcutKind.Menu);

    public static ShortcutAction Rename => new(ShortcutKind.Rename);

    public static ShortcutAction Copy => new(ShortcutKind.Copy);

    public static ShortcutAction Cut => new(ShortcutKind.Cut);

    public static ShortcutAction Paste => new(ShortcutKind.Paste);

    public static ShortcutAction Up => new(ShortcutKind.Up);

    public static ShortcutAction GoTo(string target) => new(ShortcutKind.GoTo, target);

    public static ShortcutAction Extend(int delta) => new(ShortcutKind.Extend, null, delta);

    public static ShortcutAction MailAction(string name) => new(ShortcutKind.Mail, name);

    public static ShortcutAction Cal(string name) => new(ShortcutKind.Cal, name);

    public static ShortcutAction CalView(int view) => new(ShortcutKind.CalView, null, view);

    public static ShortcutAction Move(int delta) => new(ShortcutKind.Move, null, delta);
}
