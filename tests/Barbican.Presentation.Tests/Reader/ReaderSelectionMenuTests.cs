// Mirrors: Bulwark components/email/reader-selection-menu.tsx:177-198 (Copy, a separator, the add-ons' rows; copy writes the selected text), :154-155 (blank text opens nothing).
// RD-08: the rows of MENU-SELECTION, the commands behind them, the argument that carries the text, and what the controller does with it. Owner reader.

#pragma warning disable MA0002, MA0006 // labels are compared ordinally by nature

using Barbican.Core.Common;
using Barbican.Core.Mail.Body;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Common;
using Barbican.Presentation.Menus;
using Barbican.Presentation.Reader;
using Barbican.Presentation.Tests.Menus;

namespace Barbican.Presentation.Tests.Reader;

public sealed class ReaderSelectionMenuTests
{
    private static readonly ReaderSelectionAddOn Summarise = new("pkg\u001fsummarise", "Summarise", "file", false);
    private static readonly ReaderSelectionAddOn Wipe = new("pkg\u001fwipe", "Wipe", null, true);

    private static MenuContext With(string? text) =>
        new(MenuIds.ReaderSelection, [], null, null, null, null, CommandSurface.ContextMenu) { SelectionText = text };

    [Fact]
    public void Copy_alone_when_no_add_on_contributes()
    {
        var rows = ReaderSelectionMenu.Rows(With("hello"), []);
        var copy = Assert.Single(rows);
        Assert.Equal(("Copy", CommandIds.ReaderSelectionCopy, "hello"), (copy.Label, copy.CommandId, copy.Argument));
    }

    [Fact]
    public void Copy_then_a_separator_then_the_add_ons_in_the_order_given()
    {
        var rows = ReaderSelectionMenu.Rows(With("hello"), [Summarise, Wipe]);
        Assert.Equal(["Copy", "—", "Summarise", "Wipe"], MenuKit.Labels(rows));
        Assert.All(rows.Skip(2), r => Assert.Equal(CommandIds.ReaderSelectionAddOn, r.CommandId));
        Assert.True(rows[3].Danger);
        Assert.False(rows[2].Danger);
        Assert.True(ReaderSelectionMenu.TryDecode(rows[2].Argument, out var key, out var text));
        Assert.Equal(("pkg\u001fsummarise", "hello"), (key, text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t")]
    public void Blank_text_opens_nothing(string? text) =>
        Assert.Empty(ReaderSelectionMenu.Rows(With(text), [Summarise]));

    [Fact]
    public void Text_is_cut_to_the_bridge_cap()
    {
        var oversized = new string('x', ShellMessage.MaxSelection + 50);
        Assert.Equal(ShellMessage.MaxSelection, ReaderSelectionMenu.Usable(oversized)!.Length);
        Assert.Equal(ShellMessage.MaxSelection, ReaderSelectionMenu.Rows(With(oversized), [])[0].Argument!.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nokey")]
    [InlineData("\ntext")]
    [InlineData("key\n")]
    [InlineData("key\n   ")]
    public void A_malformed_argument_is_refused(string? argument) =>
        Assert.False(ReaderSelectionMenu.TryDecode(argument, out _, out _));

    [Fact]
    public void An_oversized_key_is_refused() =>
        Assert.False(ReaderSelectionMenu.TryDecode(new string('k', ReaderSelectionMenu.MaxKeyLength + 1) + "\ntext", out _, out _));

    [Fact]
    public async Task Copy_command_puts_the_selected_text_on_the_clipboard()
    {
        var clipboard = new Clip();
        var copy = ReaderSelectionMenu.Commands(clipboard, () => null)[0];
        var context = new CommandContext(CommandSurface.ContextMenu, [], null, "selected words", null);
        Assert.True(copy.CanExecute(context));
        Assert.True((await copy.ExecuteAsync(context, CancellationToken.None)).IsOk);
        Assert.Equal(["selected words"], clipboard.Texts);
        Assert.False(copy.CanExecute(context with { Argument = "  " }));
        Assert.False((await copy.ExecuteAsync(context with { Argument = null }, CancellationToken.None)).IsOk);
        Assert.Single(clipboard.Texts);
    }

    [Fact]
    public async Task Add_on_command_runs_the_row_on_the_selected_text()
    {
        var host = new Host([Summarise]);
        var run = ReaderSelectionMenu.Commands(new Clip(), () => host)[1];
        var row = ReaderSelectionMenu.Rows(With("some words"), host.Current())[2];
        var context = new CommandContext(CommandSurface.ContextMenu, [], null, row.Argument, null);
        Assert.True(run.CanExecute(context));
        Assert.True((await run.ExecuteAsync(context, CancellationToken.None)).IsOk);
        Assert.Equal([("pkg\u001fsummarise", "some words")], host.Ran);
    }

    [Fact]
    public async Task Add_on_command_is_off_without_an_add_on_host_or_a_valid_argument()
    {
        var context = new CommandContext(CommandSurface.ContextMenu, [], null, ReaderSelectionMenu.Encode("k", "t"), null);
        var none = ReaderSelectionMenu.Commands(new Clip(), () => null)[1];
        Assert.False(none.CanExecute(context));
        Assert.False((await none.ExecuteAsync(context, CancellationToken.None)).IsOk);
        var host = new Host([]);
        var bad = ReaderSelectionMenu.Commands(new Clip(), () => host)[1];
        Assert.False(bad.CanExecute(context with { Argument = "no separator" }));
        Assert.False((await bad.ExecuteAsync(context with { Argument = "no separator" }, CancellationToken.None)).IsOk);
        Assert.Empty(host.Ran);
    }

    [Fact]
    public void The_menu_opens_at_the_anchor_with_both_commands_enabled_and_closes()
    {
        var host = new Host([Summarise]);
        var registry = new CommandRegistry(ReaderSelectionMenu.Commands(new Clip(), () => host));
        var controller = new ContextMenuController(new MenuModelBuilder([ReaderSelectionMenu.Provider(() => host)], registry), registry);
        controller.Show(With("pick me"), new MenuAnchor(40, 60, null));
        Assert.True(controller.IsOpen);
        Assert.Equal(new MenuAnchor(40, 60, null), controller.Anchor);
        Assert.All(controller.Root!.Items.Where(i => i.Kind == MenuItemKind.Item), i => Assert.True(i.Enabled));
        controller.Close();
        Assert.False(controller.IsOpen);
        controller.Show(With(" "), new MenuAnchor(40, 60, null));
        Assert.False(controller.IsOpen);
    }

    [Fact]
    public async Task Picking_copy_in_the_open_menu_copies_and_closes()
    {
        var clipboard = new Clip();
        var registry = new CommandRegistry(ReaderSelectionMenu.Commands(clipboard, () => null));
        var controller = new ContextMenuController(new MenuModelBuilder([ReaderSelectionMenu.Provider(() => null)], registry), registry);
        controller.Show(With("copy this"), new MenuAnchor(1, 2, null));
        Assert.True(await controller.PickAsync("Copy"));
        Assert.False(controller.IsOpen);
        Assert.Equal(["copy this"], clipboard.Texts);
    }

    private sealed class Clip : IClipboardService
    {
        public List<string> Texts { get; } = [];

        public void SetText(string text) => Texts.Add(text);
    }

    private sealed class Host(IReadOnlyList<ReaderSelectionAddOn> rows) : IReaderSelectionAddOns
    {
        public List<(string Key, string Text)> Ran { get; } = [];

        public IReadOnlyList<ReaderSelectionAddOn> Current() => rows;

        public Task<Result<Unit>> RunAsync(string key, string text, CancellationToken ct)
        {
            Ran.Add((key, text));
            return Task.FromResult(Result<Unit>.Ok(default));
        }
    }
}
