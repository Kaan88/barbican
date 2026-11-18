// New tests: the palette's behaviour over the model: opening reads the context now, every typed word must match, a row whose command has no handler is disabled and
// cannot run, picking runs the mapped command with the selection and closes, Tab changes the scope to a provider's, Esc closes. Rust palette.rs :246-:485. Owner menus.

#pragma warning disable MA0002, MA0006, VSTHRD003, xUnit1051 // asserts compare labels ordinally by nature; fakes hand back TaskCompletionSource tasks; the delays are one-tick yields

using Barbican.Presentation.Commands;
using Barbican.Presentation.Input;
using Barbican.Presentation.Palette;
using Barbican.Presentation.Tests.Menus;

namespace Barbican.Presentation.Tests.Palette;

public sealed class PaletteControllerTests
{
    private sealed class FixedContext(PaletteContext context) : IPaletteContextSource
    {
        public PaletteContext Current() => context;
    }

    private sealed class ScopeProvider : IPaletteProvider
    {
        public string Scope => "Search";

        public Task<IReadOnlyList<PaletteCommand>> RowsAsync(string text, PaletteContext context, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PaletteCommand>>(text.Length == 0 ? [] : [new PaletteCommand("hit:0", "Mail", "mail", "Hit for " + text, [], string.Empty)]);
    }

    private static PaletteContext Context() => new()
    {
        Calendars = true,
        SingleKeys = true,
        Dark = true,
        HasMail = true,
        Targets = [MenuKit.Target("m1")],
        Account = "me@example.org",
        Accounts = ["me@example.org", "other@example.net"],
    };

    private static (PaletteController Palette, Dictionary<string, FakeCommand> Commands) Make(params IPaletteProvider[] providers) => Make(null, providers);

    private static (PaletteController Palette, Dictionary<string, FakeCommand> Commands) Make(string? without, params IPaletteProvider[] providers)
    {
        var commands = MenuKit.AllCommands();
        if (without is not null)
        {
            commands.Remove(without);
        }

        return (new PaletteController(new CommandRegistry(commands.Values), new FixedContext(Context()), providers), commands);
    }

    [Fact]
    public async Task Opening_lists_the_open_apps_commands_first_with_group_headings()
    {
        var (palette, _) = Make();
        await palette.OpenAsync();
        Assert.True(palette.IsOpen);
        Assert.Equal("Mail", palette.Rows[0].Header);
        Assert.Equal("Reply", palette.Rows[1].Command!.Label);
        Assert.Equal(1, palette.Highlight);
    }

    [Fact]
    public async Task Every_typed_word_must_match_and_go_inbox_finds_mail()
    {
        var (palette, _) = Make();
        await palette.OpenAsync();
        await palette.SetTextAsync("go inbox");
        Assert.Equal(["Go to Mail"], palette.Rows.Where(r => !r.IsHeader).Select(r => r.Command!.Label).ToArray());
        await palette.SetTextAsync("zzz");
        Assert.DoesNotContain(palette.Rows, r => !r.IsHeader);
    }

    [Fact]
    public async Task A_row_without_a_handler_is_disabled_and_cannot_run()
    {
        var (palette, _) = Make(CommandIds.GoCalendar);
        await palette.OpenAsync();
        await palette.SetTextAsync("calendar");
        var row = palette.Rows.Single(r => !r.IsHeader && r.Command!.Id == "go:calendar");
        Assert.False(row.Enabled);
        Assert.False(await palette.PickAsync(0));
        Assert.True(palette.IsOpen);
    }

    [Fact]
    public async Task Picking_runs_the_mapped_command_with_the_selection_and_closes()
    {
        var (palette, commands) = Make();
        await palette.OpenAsync();
        await palette.SetTextAsync("reply all");
        Assert.True(await palette.PickAsync(0));
        var ran = Assert.Single(commands[CommandIds.ReplyAll].Ran);
        Assert.Equal(CommandSurface.Palette, ran.Surface);
        Assert.Equal("m1", Assert.Single(ran.Targets).EmailId);
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public async Task Switching_account_passes_the_account_as_the_argument()
    {
        var (palette, commands) = Make();
        await palette.OpenAsync();
        await palette.SetTextAsync("switch other");
        Assert.True(await palette.PickAsync(0));
        Assert.Equal("other@example.net", Assert.Single(commands[CommandIds.SwitchAccount].Ran).Argument);
    }

    [Fact]
    public async Task Arrow_keys_skip_headings_and_enter_runs_the_highlighted_row()
    {
        var (palette, commands) = Make();
        await palette.OpenAsync();
        Assert.True(palette.HandleKey(new KeyChord("ArrowDown")));
        Assert.Equal("Reply all", palette.Rows[palette.Highlight].Command!.Label);
        Assert.True(palette.HandleKey(new KeyChord("Enter")));
        await Task.Delay(50);
        Assert.Single(commands[CommandIds.ReplyAll].Ran);
        Assert.False(palette.HandleKey(new KeyChord("x")));
    }

    [Fact]
    public async Task Tab_switches_to_a_providers_scope_and_escape_closes()
    {
        var (palette, _) = Make(new ScopeProvider());
        await palette.OpenAsync();
        Assert.Equal(["Commands", "Search"], palette.Scopes);
        await palette.NextScopeAsync();
        await palette.SetTextAsync("ann");
        Assert.Equal("Hit for ann", palette.Rows.Single(r => !r.IsHeader).Command!.Label);
        Assert.True(palette.HandleKey(new KeyChord("Escape")));
        Assert.False(palette.IsOpen);
    }

    [Theory]
    [InlineData("mail:star", false, "mail.star")]
    [InlineData("mail:spam", false, "mail.spam")]
    [InlineData("go:files", false, "go.files")]
    public void Palette_ids_map_to_registry_commands(string paletteId, bool junk, string expected)
    {
        var (id, argument) = PaletteCommandMap.Resolve(new PaletteCommand(paletteId, "x", "i", "l", [], string.Empty), new PaletteContext { InJunk = junk });
        Assert.Equal(expected, id);
        Assert.Null(argument);
    }

    [Fact]
    public void Files_rows_have_no_command_until_their_milestone_and_contacts_rows_run_the_contacts_commands()
    {
        Assert.Equal("contacts.new", PaletteCommandMap.Resolve(new PaletteCommand("ct:new", "Contacts", "i", "New contact", [], string.Empty), new PaletteContext()).CommandId);
        Assert.Equal("contacts.import", PaletteCommandMap.Resolve(new PaletteCommand("ct:import", "Contacts", "i", "Import contacts", [], string.Empty), new PaletteContext()).CommandId);
        Assert.Null(PaletteCommandMap.Resolve(new PaletteCommand("fl:upload", "Files", "i", "Upload files", [], string.Empty), new PaletteContext()).CommandId);
    }
}
