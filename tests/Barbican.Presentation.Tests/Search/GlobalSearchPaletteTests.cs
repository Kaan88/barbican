// New tests (SR-03): the palette's global search over FakeJmap mail and sample contacts, events and files: one group per kind, the scope chips narrow the rows and keep the counts, an in: operator beats a chip, the chips
// cycle, the preview of the highlighted hit has real data (mail: from, folder, date and the server's marks; contact, event, file: their lines), a hit's row id opens it and a hit of an app the shell lacks cannot be opened.
// Bulwark components/global-search/global-search-palette.tsx, search-scope-chips.tsx, search-facets.tsx, search-preview.tsx.

#pragma warning disable MA0002, MA0006, VSTHRD003, xUnit1051 // asserts compare labels ordinally by nature; the delays are one-tick yields

using System.Text.Json.Nodes;
using Barbican.Core.Files;
using Barbican.Core.Search;
using Barbican.Core.Sync;
using Barbican.Presentation.Palette;
using Barbican.Presentation.Search;
using Barbican.Presentation.Tests.Actions;

namespace Barbican.Presentation.Tests.Search;

public sealed class GlobalSearchPaletteTests
{
    private sealed class FixedSources(GlobalSearchData data) : IGlobalSearchSources
    {
        public int Loads { get; private set; }

        public Task<GlobalSearchData> LoadAsync(CancellationToken ct)
        {
            Loads++;
            return Task.FromResult(data);
        }
    }

    private static GlobalSearchData Data() => new()
    {
        Cards =
        [
            JsonNode.Parse("""{"id":"c1","name":{"full":"Report Reviewer"},"emails":{"e1":{"address":"reviewer@example.org"}},"phones":{"p1":{"number":"+1 555 0100"}},"organizations":{"o1":{"name":"Example Org"}},"addressBookIds":{"b1":true},"updated":"2026-10-01T00:00:00Z"}""")!,
            JsonNode.Parse("""{"id":"c2","name":{"full":"Dana Scully"},"emails":{"e1":{"address":"dana@example.org"}},"addressBookIds":{"b1":true}}""")!,
        ],
        Books = [JsonNode.Parse("""{"id":"b1","name":"Personal"}""")!],
        Events =
        [
            JsonNode.Parse("""{"id":"ev1","uid":"u1","title":"Report review","start":"2026-11-20T10:00:00","description":"Walk through the quarterly report","locations":{"l1":{"name":"Room 4"}},"participants":{"p1":{"name":"Carol"}},"calendarIds":{"cal1":true}}""")!,
            JsonNode.Parse("""{"id":"ev2","uid":"u2","title":"Lunch","start":"2026-11-21T12:00:00","calendarIds":{"cal1":true}}""")!,
        ],
        Calendars = [JsonNode.Parse("""{"id":"cal1","name":"Work"}""")!],
        Files =
        [
            new FileEntry("d1", "acct", false, "d1", null, "Documents", true, null, 0, null, null, null),
            new FileEntry("f1", "acct", false, "f1", "d1", "report.pdf", false, "blob1", 2048, new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero), "application/pdf", null),
            new FileEntry("t0", "acct", false, ".Trash", null, ".Trash", true, null, 0, null, null, null),
            new FileEntry("t1", "acct", false, "t1", "t0", "report-old.pdf", false, "blob2", 10, null, "application/pdf", null),
        ],
    };

    private static async Task<(ActionHarness Harness, GlobalSearchPaletteProvider Provider, FixedSources Sources)> StartAsync()
    {
        var h = await ActionHarness.StartAsync();
        var sources = new FixedSources(Data());
        var provider = new GlobalSearchPaletteProvider(h.Active, new NullHookBus(), id => id.Length == 0 ? string.Empty : "Inbox", () => string.Empty, sources);
        return (h, provider, sources);
    }

    private static Task<IReadOnlyList<PaletteCommand>> RowsAsync(GlobalSearchPaletteProvider provider, string text) =>
        provider.RowsAsync(text, new PaletteContext(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task One_group_per_kind_with_a_count_chip_for_each()
    {
        var (h, provider, _) = await StartAsync();
        using var _h = h;
        var rows = await RowsAsync(provider, "report");
        Assert.Equal(["Mail", "Contacts", "Calendar", "Files"], rows.Select(r => r.Group).Distinct().ToArray());
        var facets = provider.Facets.ToDictionary(f => f.Key, f => f.Count);
        Assert.Equal(1, facets["contacts"]);
        Assert.Equal(1, facets["calendar"]);
        Assert.Equal(1, facets["files"]);
        Assert.True(facets["mail"] >= 1);
        Assert.Equal(facets["mail"] + 3, facets["all"]);
        Assert.True(provider.Facets.Single(f => f.Active).Key == "all");
        // the trashed file is no hit
        Assert.DoesNotContain(rows, r => r.Label.Contains("report-old", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_scope_chip_narrows_the_rows_and_keeps_the_counts_without_asking_again()
    {
        var (h, provider, sources) = await StartAsync();
        using var _h = h;
        _ = await RowsAsync(provider, "report");
        var calls = h.Server.ApiCalls.Count;
        var loads = sources.Loads;
        var before = provider.Facets.ToDictionary(f => f.Key, f => f.Count);

        provider.SetFacet("contacts");
        var rows = await RowsAsync(provider, "report");
        Assert.All(rows, r => Assert.Equal("Contacts", r.Group));
        Assert.Single(rows);
        Assert.Equal("contacts", provider.Facets.Single(f => f.Active).Key);
        Assert.Equal(before, provider.Facets.ToDictionary(f => f.Key, f => f.Count));
        // a chip changes what shows, not what was found
        Assert.Equal(calls, h.Server.ApiCalls.Count);
        Assert.Equal(loads, sources.Loads);

        provider.SetFacet("mail");
        Assert.All(await RowsAsync(provider, "report"), r => Assert.Equal("Mail", r.Group));
        provider.SetFacet("all");
        Assert.True((await RowsAsync(provider, "report")).Select(r => r.Group).Distinct().Count() == 4);
    }

    [Fact]
    public async Task An_in_operator_in_the_query_wins_over_the_chip()
    {
        var (h, provider, _) = await StartAsync();
        using var _h = h;
        provider.SetFacet("mail");
        var rows = await RowsAsync(provider, "in:files report");
        Assert.Single(rows);
        Assert.Equal("Files", rows[0].Group);
        Assert.Equal("files", provider.Facets.Single(f => f.Active).Key);
    }

    [Fact]
    public async Task The_chips_cycle_in_both_directions_and_wrap()
    {
        var (h, provider, _) = await StartAsync();
        using var _h = h;
        _ = await RowsAsync(provider, "report");
        Assert.Equal("mail", provider.NextFacetKey(1));
        Assert.Equal("files", provider.NextFacetKey(-1));
        provider.SetFacet("files");
        _ = await RowsAsync(provider, "report");
        Assert.Equal("all", provider.NextFacetKey(1));
        Assert.Equal("calendar", provider.NextFacetKey(-1));
    }

    [Fact]
    public async Task The_palette_controller_cycles_the_chips_with_ctrl_arrows_and_previews_the_highlighted_row()
    {
        var (h, provider, _) = await StartAsync();
        using var _h = h;
        var palette = new PaletteController(new Barbican.Presentation.Commands.CommandRegistry([Barbican.Presentation.Commands.DelegateAppCommand.Sync(Barbican.Presentation.Commands.CommandIds.OpenSearchHit, _ => { })]), new Fixed(), [provider]);
        await palette.OpenAsync(GlobalSearchPaletteProvider.ScopeName);
        await palette.SetTextAsync("report");
        Assert.True(palette.HasPreviewPane);
        Assert.NotNull(palette.Preview);
        Assert.Equal(SearchScope.Mail, palette.Preview!.Kind);

        Assert.True(palette.HandleKey(new Barbican.Presentation.Input.KeyChord("ArrowRight", Control: true)));
        await Task.Yield();
        Assert.Equal("mail", palette.Facets.Single(f => f.Active).Key);
        await palette.SetFacetAsync("calendar");
        Assert.Equal(SearchScope.Calendar, palette.Preview!.Kind);
        Assert.All(palette.Rows.Where(r => !r.IsHeader), r => Assert.Equal("Calendar", r.Command!.Group));
        // arrows keep moving the preview with the highlight
        await palette.SetFacetAsync("all");
        var first = palette.Preview!.Title;
        palette.Move(1);
        Assert.NotEqual(first, palette.Preview!.Title);
        // closing drops it
        palette.Close();
        Assert.Null(palette.Preview);
    }

    private sealed class Fixed : IPaletteContextSource
    {
        public PaletteContext Current() => new();
    }

    [Fact]
    public async Task The_mail_preview_has_from_folder_date_and_the_marks_the_server_gave()
    {
        var (h, provider, _) = await StartAsync();
        using var _h = h;
        var rows = await RowsAsync(provider, "report");
        var mail = rows.First(r => r.Group == "Mail" && r.Label.StartsWith("Report with attachments", StringComparison.Ordinal));
        var preview = provider.PreviewFor(mail)!;
        Assert.Equal("Report with attachments", preview.Title);
        Assert.Equal("Open message", preview.OpenLabel);
        Assert.Contains(preview.Lines, l => l.Icon == "mail" && l.Text.Contains("carol@example.org", StringComparison.Ordinal));
        Assert.Contains(preview.Lines, l => l.Icon == "folder" && l.Text == "Inbox");
        Assert.Contains(preview.Lines, l => l.Icon == "clock" && l.Text.StartsWith("2026-11-02", StringComparison.Ordinal));
        Assert.Contains(preview.Snippet, s => s.Marked && s.Text.Equals("report", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(h.Server.ApiCalls, c => c.Contains("SearchSnippet/get", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_contact_event_and_file_previews_carry_their_data()
    {
        var (h, provider, _) = await StartAsync();
        using var _h = h;
        var rows = await RowsAsync(provider, "report");
        var contact = provider.PreviewFor(rows.Single(r => r.Group == "Contacts"))!;
        Assert.Equal("Report Reviewer", contact.Title);
        Assert.Equal("Personal · reviewer@example.org", contact.Subtitle);
        Assert.Equal(["mail=reviewer@example.org", "phone=+1 555 0100", "building=Example Org"], contact.Lines.Select(l => l.Icon + "=" + l.Text).ToArray());

        var ev = provider.PreviewFor(rows.Single(r => r.Group == "Calendar"))!;
        Assert.Equal("Report review", ev.Title);
        Assert.Equal("Work · Room 4", ev.Subtitle);
        Assert.Equal(["clock=2026-11-20 10:00", "pin=Room 4", "text=Walk through the quarterly report", "people=Carol"], ev.Lines.Select(l => l.Icon + "=" + l.Text).ToArray());

        var file = provider.PreviewFor(rows.Single(r => r.Group == "Files"))!;
        Assert.Equal("report.pdf", file.Title);
        Assert.Equal("/Documents", file.Subtitle);
        Assert.Equal(["drive=2 KB · application/pdf", "clock=2026-11-02 09:30", "folder=/Documents"], file.Lines.Select(l => l.Icon + "=" + l.Text).ToArray());
    }

    [Fact]
    public async Task A_row_id_names_what_opening_it_does()
    {
        var (h, provider, _) = await StartAsync();
        using var _h = h;
        var rows = await RowsAsync(provider, "report");
        Assert.Equal("search-hit:contact:c1", rows.Single(r => r.Group == "Contacts").Id);
        Assert.Equal("search-hit:event:ev1:2026-11-20", rows.Single(r => r.Group == "Calendar").Id);
        Assert.Equal("search-hit:file:f:d1;f1", rows.Single(r => r.Group == "Files").Id);
        Assert.StartsWith("search-hit:mail:", rows.First(r => r.Group == "Mail").Id, StringComparison.Ordinal);
        foreach (var row in rows)
        {
            var (command, argument) = PaletteCommandMap.Resolve(row, new PaletteContext());
            Assert.Equal(Barbican.Presentation.Commands.CommandIds.OpenSearchHit, command);
            Assert.Equal(row.Id["search-hit:".Length..], argument);
        }
    }

    [Fact]
    public async Task Opening_a_hit_of_an_app_the_shell_does_not_have_is_refused_and_a_mail_hit_opens_its_message()
    {
        var (h, provider, _) = await StartAsync();
        using var _h = h;
        var opener = new SearchHitOpener(h.List, h.Sidebar);
        foreach (var argument in new[] { "contact:c1", "event:ev1:2026-11-20", "file:f:d1;f1", "file:x", "event:nope", "contact:" })
        {
            var result = await SearchCommands.OpenAsync(opener, argument);
            Assert.False(result.IsOk, argument);
        }

        await h.OpenAsync("inbox");
        var mail = (await RowsAsync(provider, "report")).First(r => r.Group == "Mail");
        var opened = await SearchCommands.OpenAsync(opener, mail.Id["search-hit:".Length..]);
        Assert.True(opened.IsOk, opened.Error?.Message);
        Assert.Equal("Report with attachments", h.List.Selected?.Summary.Subject);
    }

    [Fact]
    public async Task Nothing_typed_shows_nothing_and_a_text_no_kind_has_shows_zero_counts()
    {
        var (h, provider, sources) = await StartAsync();
        using var _h = h;
        Assert.Empty(await RowsAsync(provider, string.Empty));
        Assert.Equal(0, sources.Loads);
        Assert.Empty(await RowsAsync(provider, "zzzznothing"));
        Assert.All(provider.Facets, f => Assert.Equal(0, f.Count));
    }
}
