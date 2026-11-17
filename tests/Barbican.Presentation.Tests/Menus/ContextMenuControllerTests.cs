// New tests: the open menu: a command with no handler shows disabled, the enable state is read when the menu opens (not cached), the confirmation swaps the menu in place,
// hovering a flyout row opens it and another row closes it, the keyboard (arrows, Home, End, Enter, Left/Right, type-ahead, Escape), and picking runs the command with
// its argument. Spec menus.md conventions; Rust ctxmenu.rs :134 hover, :697 confirm. Owner menus.

#pragma warning disable MA0002, MA0006, VSTHRD003, xUnit1051 // asserts compare labels ordinally by nature; fakes hand back TaskCompletionSource tasks; the delays are one-tick yields

using Barbican.Presentation.Commands;
using Barbican.Presentation.Input;
using Barbican.Presentation.Menus;
using static Barbican.Presentation.Tests.Menus.MenuKit;

namespace Barbican.Presentation.Tests.Menus;

public sealed class ContextMenuControllerTests
{
    private readonly FakeMenuData _data = new();
    private readonly Dictionary<string, FakeCommand> _commands = AllCommands();

    private ContextMenuController Open(string menuId, MenuContext? context = null)
    {
        var controller = new ContextMenuController(Builder(_data, _commands.Values), new CommandRegistry(_commands.Values));
        controller.Show(context ?? Context(menuId, targets: Target("m1")), new MenuAnchor(100, 80, null));
        return controller;
    }

    [Fact]
    public void A_command_with_no_handler_shows_disabled()
    {
        _commands.Remove(CommandIds.Print);
        var controller = Open(MenuIds.Mail);
        var more = controller.Root!.Items.Single(r => r.Label == "More actions");
        Assert.True(more.Enabled);
        controller.Hover(controller.Root.Items.ToList().IndexOf(more), inFlyout: false);
        var print = controller.Flyout!.Items.Single(r => r.Label == "Print");
        Assert.False(print.Enabled);
        Assert.True(controller.Flyout.Items.Single(r => r.Label == "Open").Enabled);
    }

    [Fact]
    public void Enable_state_is_read_each_time_a_menu_opens()
    {
        var builder = Builder(_data, _commands.Values);
        var context = Context(MenuIds.Mail, targets: Target("m1"));
        Assert.True(builder.Build(context).Single(r => r.Label == "Archive").Enabled);
        _commands[CommandIds.Archive].Enabled = false;
        Assert.False(builder.Build(context).Single(r => r.Label == "Archive").Enabled);
        _commands[CommandIds.Archive].Enabled = true;
        Assert.True(builder.Build(context).Single(r => r.Label == "Archive").Enabled);
    }

    [Fact]
    public async Task Picking_a_row_runs_its_command_with_the_targets_and_the_argument_and_closes()
    {
        var controller = Open(MenuIds.Mail);
        Assert.True(await controller.PickAsync("Work"));
        var ran = Assert.Single(_commands[CommandIds.MoveTo].Ran);
        Assert.Equal("work", ran.Argument);
        Assert.Equal("m1", Assert.Single(ran.Targets).EmailId);
        Assert.Equal(CommandSurface.ContextMenu, ran.Surface);
        Assert.False(controller.IsOpen);
    }

    [Fact]
    public async Task A_danger_row_with_a_confirmation_swaps_the_menu_in_place_and_runs_only_after_the_second_pick()
    {
        var controller = Open(MenuIds.Mail, Context(MenuIds.Mail, targets: [Target("m1"), Target("m2")]));
        var delete = controller.Root!.Items.ToList().FindIndex(r => r.Label == "Delete");
        Assert.True(await controller.ActivateAsync(delete, inFlyout: false));
        Assert.True(controller.Confirming);
        Assert.True(controller.IsOpen);
        Assert.Equal(["Are you sure?", "Delete selected messages", "Cancel"], controller.Root.Items.Select(r => r.Label).ToArray());
        Assert.Empty(_commands[CommandIds.Delete].Ran);
        Assert.True(await controller.PickAsync("Delete selected messages"));
        Assert.Single(_commands[CommandIds.Delete].Ran);
        Assert.False(controller.IsOpen);
    }

    [Fact]
    public async Task Cancel_and_escape_leave_the_confirmation_without_running()
    {
        var controller = Open(MenuIds.Folder, Context(MenuIds.Folder, role: null, mailboxId: "work"));
        await controller.PickAsync("Delete folder");
        Assert.True(controller.Confirming);
        Assert.True(await controller.PickAsync("Cancel"));
        Assert.False(controller.IsOpen);
        Assert.Empty(_commands[CommandIds.DeleteFolder].Ran);

        controller.Show(Context(MenuIds.Folder, role: null, mailboxId: "work"), default);
        await controller.PickAsync("Delete folder");
        Assert.True(controller.HandleKey(new KeyChord("Escape")));
        Assert.False(controller.IsOpen);
        Assert.Empty(_commands[CommandIds.DeleteFolder].Ran);
    }

    [Fact]
    public void Hovering_a_flyout_row_opens_the_flyout_and_hovering_another_row_closes_it()
    {
        var controller = Open(MenuIds.Mail);
        var items = controller.Root!.Items.ToList();
        controller.Hover(items.FindIndex(r => r.Label == "Move to"), inFlyout: false);
        Assert.NotNull(controller.Flyout);
        controller.Hover(items.FindIndex(r => r.Label == "Reply"), inFlyout: false);
        Assert.Null(controller.Flyout);
    }

    [Fact]
    public void Keyboard_moves_over_enabled_rows_wraps_and_opens_flyouts_with_right()
    {
        var controller = Open(MenuIds.Mail);
        controller.HandleKey(new KeyChord("ArrowDown"));
        Assert.Equal("Reply", controller.Root!.Items[controller.Root.Highlight].Label);
        controller.HandleKey(new KeyChord("End"));
        Assert.Equal("More actions", controller.Root.Items[controller.Root.Highlight].Label);
        controller.HandleKey(new KeyChord("ArrowDown"));
        Assert.Equal("Reply", controller.Root.Items[controller.Root.Highlight].Label);
        controller.HandleKey(new KeyChord("Home"));
        controller.HandleKey(new KeyChord("ArrowUp"));
        Assert.Equal("More actions", controller.Root.Items[controller.Root.Highlight].Label);
        controller.HandleKey(new KeyChord("ArrowRight"));
        Assert.NotNull(controller.Flyout);
        controller.HandleKey(new KeyChord("ArrowDown"));
        Assert.Equal("Open", controller.Flyout!.Items[controller.Flyout.Highlight].Label);
        controller.HandleKey(new KeyChord("ArrowLeft"));
        Assert.Null(controller.Flyout);
    }

    [Fact]
    public async Task Enter_runs_the_highlighted_row_and_type_ahead_finds_a_row()
    {
        var controller = Open(MenuIds.Mail);
        controller.HandleKey(new KeyChord("a"));
        Assert.Equal("Archive", controller.Root!.Items[controller.Root.Highlight].Label);
        controller.HandleKey(new KeyChord("Enter"));
        await Task.Yield();
        Assert.Single(_commands[CommandIds.Archive].Ran);
        Assert.False(controller.IsOpen);
    }

    [Fact]
    public void Escape_closes_the_menu_and_every_key_is_taken_while_it_is_open()
    {
        var controller = Open(MenuIds.Mail);
        Assert.True(controller.HandleKey(new KeyChord("j")));
        Assert.True(controller.IsOpen);
        Assert.True(controller.HandleKey(new KeyChord("Escape")));
        Assert.False(controller.IsOpen);
        Assert.False(controller.HandleKey(new KeyChord("j")));
    }

    [Fact]
    public void A_menu_with_no_rows_does_not_open()
    {
        var controller = Open(MenuIds.Address, Context(MenuIds.Address));
        Assert.False(controller.IsOpen);
        controller.Show(Context("MENU-UNKNOWN"), default);
        Assert.False(controller.IsOpen);
    }

    [Fact]
    public void Row_heights_are_the_rust_ones()
    {
        var controller = Open(MenuIds.Mail, Context(MenuIds.Mail, targets: [Target("m1"), Target("m2")]));
        var items = controller.Root!.Items;
        Assert.Equal(24, MenuLevel.HeightOf(items[0]));
        Assert.Equal(30, MenuLevel.HeightOf(items[1]));
        Assert.Equal(9, MenuLevel.HeightOf(items.First(i => i.Kind == MenuItemKind.Separator)));
    }
}
