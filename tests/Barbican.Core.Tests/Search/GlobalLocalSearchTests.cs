// New tests (SR-03): the contacts, calendar and files hits of a global search over what the stores hold, and the mail hits' preview data (details and the server's marks) over FakeJmap.
// Bulwark lib/global-search/providers/{contacts,calendar,files}.ts, lib/search-snippet.ts.

#pragma warning disable MA0006 // asserts compare labels ordinally by nature

using System.Text.Json.Nodes;
using Barbican.Core.Files;
using Barbican.Core.Mail.Service;
using Barbican.Core.Search;
using Barbican.Core.Tests.Jmap;

namespace Barbican.Core.Tests.Search;

public sealed class GlobalLocalSearchTests
{
    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    private static FileEntry Dir(string key, string? parent, string name) => new(key, "a", false, key, parent, name, true, null, 0, null, null, null);

    private static FileEntry File(string key, string? parent, string name, long size = 1, string type = "text/plain") =>
        new(key, "a", false, key, parent, name, false, "b" + key, size, new DateTimeOffset(2026, 11, 2, 9, 5, 0, TimeSpan.Zero), type, null);

    [Fact]
    public void A_contact_is_found_by_name_address_phone_organization_or_nickname_and_groups_are_left_out()
    {
        IReadOnlyList<JsonNode> cards =
        [
            J("""{"id":"c1","name":{"full":"Ada Lovelace"},"emails":{"e":{"address":"ada@example.org"}},"phones":{"p":{"number":"+44 20 7946 0000"}},"organizations":{"o":{"name":"Analytical Engines"}},"nicknames":{"n":{"name":"Countess"}},"addressBookIds":{"b1":true},"updated":"2026-10-02T00:00:00Z"}"""),
            J("""{"id":"c2","name":{"full":"Team"},"kind":"group","emails":{"e":{"address":"ada@example.org"}}}"""),
            J("""{"id":"c3","emails":{"e":{"address":"nameless@example.org"}}}"""),
        ];
        IReadOnlyList<JsonNode> books = [J("""{"id":"b1","name":"Personal"}""")];
        foreach (var text in new[] { "lovelace", "ada@example", "7946", "engines", "countess", "ada engines" })
        {
            var hit = Assert.Single(GlobalLocalSearch.Contacts(cards, books, GlobalSearch.Parse(text)));
            Assert.Equal("c1", hit.Id);
            Assert.Equal("Ada Lovelace", hit.Title);
            Assert.Equal("Personal · ada@example.org", hit.Subtitle);
            Assert.Equal(SearchScope.Contacts, hit.Kind);
        }

        var nameless = Assert.Single(GlobalLocalSearch.Contacts(cards, books, GlobalSearch.Parse("nameless")));
        Assert.Equal("nameless@example.org", nameless.Title);
        Assert.Equal("", nameless.Subtitle);
        Assert.Empty(GlobalLocalSearch.Contacts(cards, books, GlobalSearch.Parse("nobody")));
        Assert.Empty(GlobalLocalSearch.Contacts(cards, books, GlobalSearch.Parse(string.Empty)));
    }

    [Fact]
    public void An_event_is_found_by_title_description_place_or_guest_once_per_series_within_the_day_bounds()
    {
        IReadOnlyList<JsonNode> events =
        [
            J("""{"id":"e1","uid":"u1","title":"Design review","start":"2026-11-20T10:00:00","description":"Walk through the mocks","locations":{"l":{"name":"Room 4"}},"participants":{"p":{"name":"Carol"},"q":{"email":"dan@example.org"}},"calendarIds":{"c1":true},"recurrenceRules":[{"frequency":"weekly"}]}"""),
            J("""{"id":"e1|2026-11-27","uid":"u1","title":"Design review","start":"2026-11-27T10:00:00","calendarIds":{"c1":true}}"""),
            J("""{"id":"e2","uid":"u2","title":"Lunch","start":"2026-12-05T12:00:00","showWithoutTime":true,"calendarIds":{"c1":true}}"""),
            J("""{"id":"t1","uid":"u3","title":"Review taxes","@type":"Task","due":"2026-11-20T10:00:00"}"""),
        ];
        IReadOnlyList<JsonNode> calendars = [J("""{"id":"c1","name":"Work"}""")];
        foreach (var text in new[] { "design", "mocks", "room", "carol", "dan@example" })
        {
            var hit = Assert.Single(GlobalLocalSearch.Events(events, calendars, GlobalSearch.Parse(text)));
            Assert.Equal("e1", hit.Id);
            Assert.True(hit.IsRecurring);
            Assert.Equal("2026-11-20", hit.Place);
            Assert.Equal("Work · Room 4", hit.Subtitle);
            Assert.Equal("2026-11-20 10:00", hit.Details[0].Text);
        }

        Assert.Equal(["Lunch"], GlobalLocalSearch.Events(events, calendars, GlobalSearch.Parse("lunch")).Select(h => h.Title).ToArray());
        Assert.Equal("2026-12-05 (all day)", GlobalLocalSearch.Events(events, calendars, GlobalSearch.Parse("lunch"))[0].Details[0].Text);
        // tasks are no events; after: and before: compare the start day
        Assert.DoesNotContain(GlobalLocalSearch.Events(events, calendars, GlobalSearch.Parse("review")), h => h.Id == "t1");
        Assert.Equal(["e1|2026-11-27"], GlobalLocalSearch.Events(events, calendars, GlobalSearch.Parse("design after:2026-11-21")).Select(h => h.Id).ToArray());
        Assert.Empty(GlobalLocalSearch.Events(events, calendars, GlobalSearch.Parse("design after:2026-11-28")));
        Assert.Empty(GlobalLocalSearch.Events(events, calendars, GlobalSearch.Parse("design before:2026-11-19")));
        Assert.Single(GlobalLocalSearch.Events(events, calendars, GlobalSearch.Parse("design after:2026-11-20 before:2026-11-20")));
    }

    [Fact]
    public void A_file_hit_has_its_path_and_open_place_trash_is_skipped_and_the_word_folder_matches_directories()
    {
        IReadOnlyList<FileEntry> entries =
        [
            Dir("d1", null, "Projects"),
            Dir("d2", "d1", "2026"),
            File("f1", "d2", "budget.xlsx", 3000, "application/vnd.ms-excel"),
            Dir("t0", null, GlobalSearch.FilesTrashName),
            File("t1", "t0", "budget-old.xlsx"),
        ];
        var budget = Assert.Single(GlobalLocalSearch.Files(entries, GlobalSearch.Parse("budget")));
        Assert.Equal("f1", budget.Id);
        Assert.Equal("/Projects/2026", budget.Subtitle);
        Assert.Equal("d2", budget.Place);
        Assert.False(budget.IsFolder);
        Assert.Equal(["drive=2.9 KB · application/vnd.ms-excel", "clock=2026-11-02 09:05", "folder=/Projects/2026"], budget.Details.Select(d => d.Icon + "=" + d.Text).ToArray());

        var folders = GlobalLocalSearch.Files(entries, GlobalSearch.Parse("folder"));
        Assert.Equal(["d1", "d2"], folders.Select(h => h.Id).ToArray());
        Assert.All(folders, f => Assert.True(f.IsFolder));
        Assert.Equal(folders[1].Id, folders[1].Place);
        Assert.Empty(GlobalLocalSearch.Files(entries, GlobalSearch.Parse(".Trash")));
    }

    [Fact]
    public void Sizes_and_instants_read_as_the_preview_shows_them()
    {
        Assert.Equal("0 B", GlobalLocalSearch.Size(0));
        Assert.Equal("1023 B", GlobalLocalSearch.Size(1023));
        Assert.Equal("1 KB", GlobalLocalSearch.Size(1024));
        Assert.Equal("1.5 MB", GlobalLocalSearch.Size(1024 * 1024 * 3 / 2));
        Assert.Equal("2026-11-02 09:30", GlobalLocalSearch.FormatInstant("2026-11-02T09:30:00Z"));
        Assert.Equal("soon", GlobalLocalSearch.FormatInstant("soon"));
    }

    [Fact]
    public async Task A_mail_hit_carries_its_preview_data_and_the_marks_of_the_server()
    {
        var f = await Fake.StartAsync();
        var mail = new MailService(f.Client, TimeProvider.System);
        var roles = (await mail.GetRoleIdsAsync(TestContext.Current.CancellationToken)).Value!;
        var parsed = GlobalSearch.Parse("report");
        var hits = await GlobalMailSearch.RunAsync(f.Client, parsed, roles, [], id => id.Length == 0 ? string.Empty : "Inbox", TestContext.Current.CancellationToken);
        Assert.True(hits.IsOk, hits.Error?.Message);
        var hit = hits.Value!.Single(h => h.Title == "Report with attachments");
        Assert.Contains(hit.Details, d => d.Icon == "mail" && d.Text == "Carol <carol@example.org>");
        Assert.Contains(hit.Details, d => d.Icon == "folder" && d.Text == "Inbox");
        Assert.Contains(hit.Details, d => d.Icon == "clock" && d.Text.StartsWith("2026-11-02", StringComparison.Ordinal));
        Assert.Contains(hit.SubjectSnippet, s => s.Marked && s.Text.Equals("Report", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hit.Snippet, s => s.Marked);
        Assert.Contains("report", hit.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(f.Server.ApiCalls, c => c.Contains("SearchSnippet/get", StringComparison.Ordinal));

        // a search with only operators has no text for the server to mark
        var plain = await GlobalMailSearch.RunAsync(f.Client, GlobalSearch.Parse("from:carol"), roles, [], _ => string.Empty, TestContext.Current.CancellationToken);
        Assert.All(plain.Value!, h => Assert.Empty(h.Snippet));
    }
}
