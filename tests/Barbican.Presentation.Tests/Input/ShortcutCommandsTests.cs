// New tests: the KeyRouter runs the keys of the shortcut tables as registry commands (the M1 keys stay as KeyRouterTests have them): ? and Ctrl+K open the sheet and the palette,
// single mail keys act on the selection, a key with no handler is not ours, single-key shortcuts off, an open menu or dialog takes every key (I-168), Backspace never deletes
// (T-260). Owner menus.

#pragma warning disable MA0002, MA0006, VSTHRD003, xUnit1051 // asserts compare labels ordinally by nature; fakes hand back TaskCompletionSource tasks; the delays are one-tick yields

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Input;
using Barbican.Presentation.Tests.Menus;

namespace Barbican.Presentation.Tests.Input;

public sealed class ShortcutCommandsTests
{
    private sealed class Selection(params MailTarget[] targets) : IMailSelection
    {
        public IReadOnlyList<MailTarget> Current { get; set; } = targets;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class Surface : IModalSurface
    {
        public bool IsOpen { get; set; }

        public List<KeyChord> Keys { get; } = [];

        public bool HandleKey(KeyChord chord)
        {
            Keys.Add(chord);
            return true;
        }
    }

    private static (KeyRouter Router, Dictionary<string, FakeCommand> Commands, Selection Selection, Surface Modal) Make(string prefs = "{}", bool selected = true, string? without = null)
    {
        var h = new Support.ListHarness(prefs);
        var commands = MenuKit.AllCommands();
        if (without is not null)
        {
            commands.Remove(without);
        }

        var selection = selected ? new Selection(MenuKit.Target("m1")) : new Selection();
        var modal = new Surface();
        var router = new KeyRouter(h.List, h.Sidebar, h.Shell, h.Prefs, h.System, h.Bus, h.Time,
            new ShortcutCommands(new CommandRegistry(commands.Values), selection), [modal]);
        return (router, commands, selection, modal);
    }

    [Fact]
    public async Task Question_mark_and_ctrl_k_run_the_sheet_and_the_palette()
    {
        var (router, commands, _, _) = Make();
        Assert.True(router.Handle(new KeyChord("?", Shift: true), KeySource.Keyboard));
        Assert.True(router.Handle(new KeyChord("k", Control: true), KeySource.Keyboard));
        await Task.Delay(30);
        Assert.Single(commands[CommandIds.ShowShortcuts].Ran);
        Assert.Single(commands[CommandIds.OpenPalette].Ran);
    }

    [Fact]
    public async Task Single_mail_keys_act_on_the_selection()
    {
        var (router, commands, _, _) = Make();
        foreach (var (key, id) in new[] { ("r", CommandIds.Reply), ("a", CommandIds.ReplyAll), ("f", CommandIds.Forward), ("e", CommandIds.Archive), ("#", CommandIds.Delete), ("s", CommandIds.ToggleStar), ("!", CommandIds.ReportSpam), ("u", CommandIds.MarkUnread), ("I", CommandIds.MarkRead) })
        {
            Assert.True(router.Handle(new KeyChord(key), KeySource.Keyboard), key);
            await Task.Delay(10);
            var ran = Assert.Single(commands[id].Ran);
            Assert.Equal("m1", Assert.Single(ran.Targets).EmailId);
            Assert.Equal(CommandSurface.Keyboard, ran.Surface);
        }
    }

    [Fact]
    public void Mail_keys_need_a_selection_and_a_handler()
    {
        var (none, _, _, _) = Make(selected: false);
        Assert.False(none.Handle(new KeyChord("r"), KeySource.Keyboard));

        var (router, commands, _, _) = Make(without: CommandIds.Forward);
        commands[CommandIds.Archive].Enabled = false;
        Assert.False(router.Handle(new KeyChord("e"), KeySource.Keyboard));
        Assert.False(router.Handle(new KeyChord("f"), KeySource.Keyboard));
    }

    [Fact]
    public void Backspace_never_deletes()
    {
        var (router, commands, _, _) = Make();
        Assert.False(router.Handle(new KeyChord("Backspace"), KeySource.Keyboard));
        Assert.Empty(commands[CommandIds.Delete].Ran);
    }

    [Fact]
    public void With_single_keys_off_only_ctrl_and_named_keys_work()
    {
        var (router, _, _, _) = Make("""{"singleKeyShortcuts": false}""");
        Assert.False(router.Handle(new KeyChord("?", Shift: true), KeySource.Keyboard));
        Assert.False(router.Handle(new KeyChord("r"), KeySource.Keyboard));
        Assert.True(router.Handle(new KeyChord("k", Control: true), KeySource.Keyboard));
    }

    [Fact]
    public void Alt_shift_a_opens_the_accessibility_options_when_a_handler_exists()
    {
        var (router, commands, _, _) = Make();
        Assert.True(router.Handle(new KeyChord("A", Shift: true, Alt: true), KeySource.Keyboard));
        Assert.Single(commands[CommandIds.OpenA11y].Ran);
    }

    [Fact]
    public void An_open_surface_takes_every_key_and_no_app_key_runs()
    {
        var (router, commands, _, modal) = Make();
        modal.IsOpen = true;
        Assert.True(router.Handle(new KeyChord("j"), KeySource.Automation));
        Assert.True(router.Handle(new KeyChord("k", Control: true), KeySource.Keyboard));
        Assert.Equal(2, modal.Keys.Count);
        Assert.Empty(commands[CommandIds.OpenPalette].Ran);
        modal.IsOpen = false;
        Assert.True(router.Handle(new KeyChord("k", Control: true), KeySource.Keyboard));
        Assert.Single(commands[CommandIds.OpenPalette].Ran);
    }

    [Fact]
    public void Key_texts_follow_the_slint_texts_the_tables_read()
    {
        Assert.Equal("\n", ShortcutCommands.TextOf(new KeyChord("Enter")));
        Assert.Equal(ShortcutKeys.Down, ShortcutCommands.TextOf(new KeyChord("ArrowDown")));
        Assert.Equal(ShortcutKeys.Delete, ShortcutCommands.TextOf(new KeyChord("Delete")));
        Assert.Equal("?", ShortcutCommands.TextOf(new KeyChord("?", Shift: true)));
        Assert.Null(ShortcutCommands.TextOf(new KeyChord("PageDown")));
    }

    [Fact]
    public async Task Run_reports_through_the_command_result()
    {
        var (_, commands, selection, _) = Make();
        var commandRegistry = new CommandRegistry(commands.Values);
        var shortcuts = new ShortcutCommands(commandRegistry, selection);
        Assert.True(shortcuts.TryResolve(new KeyChord("e"), Barbican.Presentation.Shell.AppId.Mail, true, out var id));
        Assert.Equal(CommandIds.Archive, id);
        Assert.True(shortcuts.Run(id));
        await Task.Delay(10);
        Assert.Single(commands[CommandIds.Archive].Ran);
        Assert.False(shortcuts.Run("nope"));
    }
}
