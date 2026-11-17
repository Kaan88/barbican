// Mirrors: Rust shortcuts.rs:105 is_single_key, :108 resolve, :138 mail, :172 mail_key_action, :370 alternatives, :384 help_rows, :602 step;
// Bulwark lib/keyboard-shortcuts/{index,mail,calendar,contacts,files,settings,types}.ts, hooks/use-keyboard-shortcuts.ts, lib/keyboard.ts:96-101
// (the typed character wins, R2-11). Stub from stage F of M2; owner menus. The existing KeyRouter (M1) keeps working beside this table.

using Barbican.Core.Common;

namespace Barbican.Presentation.Input;

/// <summary>The shortcut tables as pure functions over key texts.</summary>
public static class ShortcutTables
{
    /// <summary>A printable ASCII key that only counts as a single-key shortcut (WCAG 2.1.4), which Settings can turn off.</summary>
    public static bool IsSingleKey(string text)
    {
        throw new PortPendingException("shortcuts.rs:105 is_single_key");
    }

    /// <summary>Turns a key press into an action; <paramref name="text"/> already has shift applied.</summary>
    public static ShortcutAction Resolve(ShortcutPlace place, ShortcutContext context, string text, bool shift, bool ctrl, bool alt)
    {
        throw new PortPendingException("shortcuts.rs:108 resolve");
    }

    /// <summary>The mail command a single key stands for outside the table ("I" read, "u" unread, ...), or null.</summary>
    public static string? MailKeyAction(string text)
    {
        throw new PortPendingException("shortcuts.rs:172 mail_key_action");
    }

    /// <summary>The index a Move/First/Last action lands on in a list of ids, or null for an empty list.</summary>
    public static int? Step(IReadOnlyList<string> ids, string current, ShortcutAction action)
    {
        throw new PortPendingException("shortcuts.rs:602 step");
    }

    /// <summary>Splits "j / down" style help text into its alternatives ("/" alone stays one key).</summary>
    public static IReadOnlyList<string> Alternatives(string keys)
    {
        throw new PortPendingException("shortcuts.rs:370 alternatives");
    }

    /// <summary>The flattened rows of one help tab: a header row per section, then its entries; general keys listed once per tab.</summary>
    public static IReadOnlyList<HelpRow> HelpRows(int tab)
    {
        throw new PortPendingException("shortcuts.rs:384 help_rows");
    }
}
