// Mirrors: Rust shortcuts.rs resolve :108 / mail_key_action :172 (the single-key actions of an open message) and the palette's key hints; Bulwark hooks/use-keyboard-shortcuts.ts
// and lib/command-registry.ts:53-96 (a shortcut runs a registered command by id). The M1 KeyRouter keeps its own keys; this resolves the rest through ShortcutTables and the
// ICommandRegistry. A key whose command has no handler (or cannot run now) is not ours: it falls through. Owner menus.

using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Input;

/// <summary>Turns the keys the M1 router does not know into registry commands.</summary>
public sealed class ShortcutCommands(ICommandRegistry registry, IMailSelection selection)
{
    /// <summary>The command a chord runs, when the tables name one, a handler exists and it can run now.</summary>
    public bool TryResolve(KeyChord chord, AppId app, bool singleKeys, out string commandId)
    {
        commandId = string.Empty;
        var place = app switch
        {
            AppId.Calendar => ShortcutPlace.Calendar,
            AppId.Contacts => ShortcutPlace.Contacts,
            AppId.Files => ShortcutPlace.Files,
            AppId.Settings => ShortcutPlace.Settings,
            _ => ShortcutPlace.Mail,
        };
        var text = TextOf(chord);
        if (text is null)
        {
            return false;
        }

        var hasMail = selection.Current.Count > 0;
        var context = new ShortcutContext { HasMail = hasMail, SingleKeys = singleKeys };
        var action = ShortcutTables.Resolve(place, context, text, chord.Shift, chord.Control, chord.Alt);
        var id = CommandFor(action);
        if (id is null && action.Kind == ShortcutKind.Pass && place == ShortcutPlace.Mail && hasMail && singleKeys && !chord.Control && !chord.Alt)
        {
            id = MailCommand(ShortcutTables.MailKeyAction(text));
        }

        if (id is null || !registry.TryGet(id, out var command) || !command.CanExecute(ContextOf(id)))
        {
            return false;
        }

        commandId = id;
        return true;
    }

    /// <summary>Runs a command resolved by <see cref="TryResolve"/>; the result of the command is its own to show.</summary>
    public bool Run(string commandId)
    {
        if (!registry.TryGet(commandId, out var command))
        {
            return false;
        }

        _ = command.ExecuteAsync(ContextOf(commandId), CancellationToken.None);
        return true;
    }

    /// <summary>The key text the tables read for a chord (Slint key text with shift applied), or null for a key they never read.</summary>
    public static string? TextOf(KeyChord chord)
    {
        var key = chord.Key ?? string.Empty;
        return key.ToLowerInvariant() switch
        {
            "enter" or "return" => "\n",
            "escape" or "esc" => ShortcutKeys.Escape,
            "arrowdown" or "down" => ShortcutKeys.Down,
            "arrowup" or "up" => ShortcutKeys.Up,
            "arrowleft" or "left" => ShortcutKeys.Left,
            "arrowright" or "right" => ShortcutKeys.Right,
            "home" => ShortcutKeys.Home,
            "end" => ShortcutKeys.End,
            "delete" or "del" => ShortcutKeys.Delete,
            "backspace" => ShortcutKeys.Backspace,
            "f2" => ShortcutKeys.F2,
            "f5" => ShortcutKeys.F5,
            "f9" => ShortcutKeys.F9,
            "f10" => ShortcutKeys.F10,
            _ => key.Length == 1 ? key : null,
        };
    }

    private CommandContext ContextOf(string id)
    {
        _ = id;
        var targets = selection.Current;
        return new CommandContext(CommandSurface.Keyboard, targets, null, null, targets.Count > 0 ? targets[0].AccountKey : null);
    }

    private static string? CommandFor(ShortcutAction action) => action.Kind switch
    {
        ShortcutKind.Palette => CommandIds.OpenPalette,
        ShortcutKind.Help => CommandIds.ShowShortcuts,
        ShortcutKind.A11y => CommandIds.OpenA11y,
        ShortcutKind.Refresh => CommandIds.RefreshList,
        ShortcutKind.Compose => CommandIds.Compose,
        ShortcutKind.ToggleSelect => CommandIds.ToggleSelect,
        ShortcutKind.Undo => CommandIds.Undo,
        ShortcutKind.Extend => action.Number > 0 ? CommandIds.ExtendDown : CommandIds.ExtendUp,
        ShortcutKind.Mail => MailCommand(action.Name),
        _ => null,
    };

    private static string? MailCommand(string? name) => name switch
    {
        "reply" => CommandIds.Reply,
        "reply-all" => CommandIds.ReplyAll,
        "forward" => CommandIds.Forward,
        "star" => CommandIds.ToggleStar,
        "archive" => CommandIds.Archive,
        "delete" => CommandIds.Delete,
        "unread!" => CommandIds.MarkUnread,
        "read!" => CommandIds.MarkRead,
        "spam" => CommandIds.ReportSpam,
        _ => null,
    };
}
