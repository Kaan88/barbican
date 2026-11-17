// Mirrors: Rust shortcuts.rs (resolve :108, mail :138, is_single_key :105, GO_TO_SEQUENCE :31, step :602); Bulwark hooks/use-keyboard-shortcuts.ts;
// spec B-KEY-006, B-KEY-009, B-KEY-013, B-KEY-021; hooks onBeforeShortcut (intercept) and onAfterShortcut (observer). Owner mailcore.
//
// M1 keys: j / k / Down / Up (next, previous message), Enter or o (open), Escape (clear the search, else close the reader), / and Ctrl+E (search),
// Shift+G (refresh the list, as Bulwark and Rust), g then i / s / t / d / a (go to a folder), Ctrl+1..5 (the apps that exist). Everything else is not ours (false).

using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Settings.Accessibility;
using Barbican.Core.Storage;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Input;

/// <summary>Turns key chords from every source (real KeyDown, the WebView's forwarded keys, automation) into list, folder and app actions.</summary>
public sealed class KeyRouter : IKeyRouter
{
    /// <summary>How long a pending <c>g</c> waits for its second key (Bulwark GO_TO_SEQUENCE_MS).</summary>
    public static readonly TimeSpan GoToSequence = TimeSpan.FromMilliseconds(1500);

    private readonly MessageListViewModel _list;
    private readonly SidebarViewModel _sidebar;
    private readonly ShellViewModel _shell;
    private readonly IPrefsStore _prefs;
    private readonly ISystemAccessibilitySource _system;
    private readonly IHookBus _hooks;
    private readonly TimeProvider _time;
    private readonly ShortcutCommands? _commands;
    private readonly IReadOnlyList<IModalSurface> _modals;
    private long? _goArmedAt;

    public KeyRouter(
        MessageListViewModel list,
        SidebarViewModel sidebar,
        ShellViewModel shell,
        IPrefsStore prefs,
        ISystemAccessibilitySource system,
        IHookBus hooks,
        TimeProvider time,
        ShortcutCommands? commands = null,
        IEnumerable<IModalSurface>? modals = null)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(sidebar);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(time);
        _list = list;
        _sidebar = sidebar;
        _shell = shell;
        _prefs = prefs;
        _system = system;
        _hooks = hooks;
        _time = time;
        _commands = commands;
        _modals = modals?.ToList() ?? [];
    }

    /// <inheritdoc />
    public bool Handle(KeyChord chord, KeySource source)
    {
        // an open menu, dialog, palette or sheet gets the key and no app key runs behind it (Bulwark lib/keyboard.ts:355, I-168)
        foreach (var modal in _modals)
        {
            if (modal.IsOpen)
            {
                return modal.HandleKey(chord);
            }
        }

        var (name, shift) = Normalize(chord);
        if (chord.Alt)
        {
            // no M1 mail key uses Alt, and AltGr (Ctrl+Alt on Windows) types characters on many layouts (Fable F-16); the tables read Alt+Shift+A
            return TryCommands(chord);
        }

        if (source == KeySource.Keyboard && !chord.Control && name is "down" or "up")
        {
            // a focused list moves by itself on a real arrow key; routing it too would move two rows (Fable F-16). The WebView and
            // automation have no list focus, so their arrows are ours.
            return false;
        }

        var single = IsSingleKey(name) && !chord.Control;
        if (single && !A11y.From(_prefs.Current, _system.Current).SingleKeys)
        {
            return false;
        }

        var action = chord.Control ? ResolveControl(name, shift) : Resolve(name, shift);
        if (action is null)
        {
            // an unrelated key ends a pending g
            _goArmedAt = null;
            return TryCommands(chord);
        }

        var allowed = _hooks.AllowAsync(HookNames.OnBeforeShortcut, () => new ShortcutPayload(Spell(chord), action), CancellationToken.None);

        // an interceptor that needs time cannot hold up a key press: only an answer that is already there can stop the action
        if (allowed.IsCompleted && !allowed.GetAwaiter().GetResult())
        {
            return true;
        }

        var handled = Perform(action);
        if (handled)
        {
            _hooks.Fire(HookNames.OnAfterShortcut, () => new ShortcutPayload(Spell(chord), action));
        }

        return handled;
    }

    // the keys of the shortcut tables (M2 menus): a command by id, when a handler exists and it can run now
    private bool TryCommands(KeyChord chord)
    {
        if (_commands is null || !_commands.TryResolve(chord, _shell.ActiveApp, A11y.From(_prefs.Current, _system.Current).SingleKeys, out var id))
        {
            return false;
        }

        var action = "cmd:" + id;
        var allowed = _hooks.AllowAsync(HookNames.OnBeforeShortcut, () => new ShortcutPayload(Spell(chord), action), CancellationToken.None);
        if (allowed.IsCompleted && !allowed.GetAwaiter().GetResult())
        {
            return true;
        }

        var handled = _commands.Run(id);
        if (handled)
        {
            _hooks.Fire(HookNames.OnAfterShortcut, () => new ShortcutPayload(Spell(chord), action));
        }

        return handled;
    }

    private static string Spell(KeyChord c) =>
        (c.Control ? "Ctrl+" : string.Empty) + (c.Alt ? "Alt+" : string.Empty) + (c.Shift ? "Shift+" : string.Empty) + c.Key;

    /// <summary>A printable key that only counts as a "single-key shortcut" (WCAG 2.1.4), which Settings > Accessibility can turn off.</summary>
    private static bool IsSingleKey(string name) => name.Length == 1 && name[0] is > ' ' and < '\u007f';

    private static (string Name, bool Shift) Normalize(KeyChord chord)
    {
        var key = chord.Key ?? string.Empty;
        var shift = chord.Shift;
        if (key.Length == 1 && char.IsAsciiLetterUpper(key[0]))
        {
            shift = true;
            key = key.ToLowerInvariant();
        }
        else if (key.Length == 1 && char.IsAsciiLetterLower(key[0]))
        {
            key = key.ToLowerInvariant();
        }

        var name = key.ToLowerInvariant() switch
        {
            "arrowdown" or "down" => "down",
            "arrowup" or "up" => "up",
            "return" or "enter" => "enter",
            "esc" or "escape" => "escape",
            var other => other.Length == 1 ? key : other,
        };
        return (name, shift);
    }

    private string? Resolve(string name, bool shift)
    {
        // the second half of a g sequence comes before the single keys, so s / t / a / d jump
        if (_goArmedAt is { } armed)
        {
            _goArmedAt = null;
            if (_time.GetElapsedTime(armed) < GoToSequence && !shift)
            {
                var target = name switch { "i" => "go-inbox", "s" => "go-starred", "t" => "go-sent", "d" => "go-drafts", "a" => "go-all", _ => null };
                if (target is not null)
                {
                    return target;
                }
            }
        }

        return name switch
        {
            "j" when !shift => "next-message",
            "down" when !shift => "next-message",
            "k" when !shift => "previous-message",
            "up" when !shift => "previous-message",
            "g" when shift => "refresh",
            "g" => "arm-go",
            "enter" or "o" => "open",
            "escape" => "close",
            "/" => "search",
            _ => null,
        };
    }

    private static string? ResolveControl(string name, bool shift)
    {
        if (shift)
        {
            return null;
        }

        return name switch
        {
            "e" => "search",
            "1" => "app-mail",
            "2" => "app-calendar",
            "3" => "app-contacts",
            "4" => "app-files",
            "5" => "app-settings",
            _ => null,
        };
    }

    private bool Perform(string action)
    {
        switch (action)
        {
            case "next-message":
                return _list.MoveSelection(1) || _list.Rows.Count > 0;
            case "previous-message":
                return _list.MoveSelection(-1) || _list.Rows.Count > 0;
            case "refresh":
                // Bulwark lib/keyboard-shortcuts/mail.ts:34 and Rust shortcuts.rs:750 ("Shift+G stays refresh"): the list is read again, rows stay until it arrives
                _ = _list.ReloadAsync();
                return true;
            case "open":
                return _list.OpenSelected();
            case "close":
                if (_list.SearchText.Length > 0)
                {
                    _ = _list.SearchAsync(string.Empty);
                    return true;
                }

                // Bulwark hooks/use-keyboard-shortcuts.ts:217-221: Escape runs onCloseEmail and onDeselectAll together, so the ticks go too
                // (M2 verify: they stayed, and the selection bar with them)
                _list.CloseReader();
                _list.ClearChecked();
                return true;
            case "search":
                _list.RequestSearchFocus();
                return true;
            case "arm-go":
                _goArmedAt = _time.GetTimestamp();
                return true;
            case "go-inbox":
                return _sidebar.GoToRole(MailboxRole.Inbox);
            case "go-starred":
                return _sidebar.GoToRole(MailboxRole.Flagged);
            case "go-sent":
                return _sidebar.GoToRole(MailboxRole.Sent);
            case "go-drafts":
                return _sidebar.GoToRole(MailboxRole.Drafts);
            case "go-all":
                return _sidebar.GoToRole(MailboxRole.All) || _sidebar.GoToRole(MailboxRole.Archive);
            default:
                return PerformApp(action);
        }
    }

    private bool PerformApp(string action) => action switch
    {
        "app-mail" => _shell.TryActivate(AppId.Mail),
        "app-calendar" => _shell.TryActivate(AppId.Calendar),
        "app-contacts" => _shell.TryActivate(AppId.Contacts),
        "app-files" => _shell.TryActivate(AppId.Files),
        "app-settings" => _shell.TryActivate(AppId.Settings),
        _ => false,
    };
}
