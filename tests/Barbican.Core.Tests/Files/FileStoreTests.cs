// Not Rust rows: B-FILE-001, B-FILE-003..005, B-FILE-014, J-075 (FileNode: query_all and chunked get, client-side parent filter, @type on create for a draft-18 server), G-044 (files live) against FakeJmap; D-M5-09 (favourites and recent in files.json). M5 stream files-data.

#pragma warning disable MA0006, MA0002, VSTHRD103 // test assertions compare ordinal string literals and read small scratch files synchronously

using System.Text.Json.Nodes;
using Barbican.Core.Files;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Files;

public sealed class FileStoreTests
{
    [Fact]
    public async Task The_store_loads_every_node_of_the_login_and_of_a_shared_account()
    {
        using var rig = await FileRig.CreateAsync();
        var snap = await rig.LoadAsync();

        // the user's tree: bare ids; the shared tree: "account|id", its parents too (B-FILE-003)
        var march = snap.Entries.Single(e => e.Name == "march.txt");
        Assert.Equal("I1", march.Key);
        Assert.Equal(FakeSeed.FilesInvoicesId, march.ParentKey);
        Assert.False(march.Shared);
        Assert.Equal(14, march.Size);
        var plan = snap.Entries.Single(e => e.Name == "plan.md");
        Assert.Equal("s1|S2", plan.Key);
        Assert.Equal("s1|S1", plan.ParentKey);
        Assert.True(plan.Shared);
        Assert.NotNull(plan.MyRights);
        Assert.Null(snap.Entries.Single(e => e.Name == "Team").ParentKey);
        Assert.False(snap.Draft18);
        Assert.NotNull(snap.State);
        Assert.Same(snap, rig.Store.Snapshot(rig.Key));
    }

    [Fact]
    public async Task A_big_tree_is_read_in_chunks_of_maxObjectsInGet()
    {
        using var rig = await FileRig.CreateAsync(new FakeFiles { SeedM5 = true, BulkNodes = 3000 }, options: new FakeJmapOptions { MaxObjectsInGet = 500, QueryLimitCap = 1000 });
        var snap = await rig.LoadAsync();

        // the fake refuses a get of more than 500 ids and caps a query page at 1000, so all of them arriving proves the paging and the chunking
        Assert.Equal(3000, snap.Entries.Count(e => e.ParentKey == "B0"));
        Assert.Contains(snap.Entries, e => e.Name == "file-03000.txt");
    }

    [Fact]
    public async Task Both_server_shapes_load_and_the_draft_is_noticed()
    {
        using var rig = await FileRig.CreateAsync(new FakeFiles { SeedM5 = true, Draft18 = true });
        var snap = await rig.LoadAsync();

        Assert.True(snap.Draft18);
        Assert.True(snap.Entries.Single(e => e.Name == "Documents").IsDirectory);
        var notes = snap.Entries.Single(e => e.Name == "notes.txt");
        Assert.False(notes.IsDirectory);
        Assert.Equal("text/plain", notes.MediaType);
    }

    [Fact]
    public async Task The_name_rules_of_the_capability_come_with_the_snapshot()
    {
        using var rig = await FileRig.CreateAsync(new FakeFiles { SeedM5 = true, NameRules = true });
        var snap = await rig.LoadAsync();

        Assert.NotNull(snap.Rules);
        Assert.Equal(255, snap.Rules.MaxNameBytes);
        Assert.Equal("reserved", FileNameChecks.ProblemOf("con.txt", snap.Rules)!.Kind);
    }

    [Fact]
    public async Task A_login_without_file_storage_has_an_empty_snapshot()
    {
        using var rig = await FileRig.CreateAsync(capability: false);
        var snap = await rig.LoadAsync();

        Assert.Empty(snap.Entries);
        Assert.Null(snap.State);
    }

    [Fact]
    public async Task Refresh_applies_changes_made_elsewhere()
    {
        using var rig = await FileRig.CreateAsync();
        _ = await rig.LoadAsync();
        var seen = new List<string>();
        rig.Store.Changed += (_, e) => seen.AddRange(e.Keys);

        // another client creates a folder, renames a file and destroys one
        var set = await rig.CallAsync("FileNode/set", new JsonObject
        {
            ["accountId"] = rig.Server.Options.AccountId,
            ["create"] = new JsonObject { ["x"] = new JsonObject { ["parentId"] = null, ["name"] = "Elsewhere" } },
            ["update"] = new JsonObject { ["N1"] = new JsonObject { ["name"] = "minutes.txt" } },
            ["destroy"] = new JsonArray("R1"),
        });
        Assert.True(set.IsOk);
        Assert.True((await rig.Store.RefreshAsync(rig.Key, TestContext.Current.CancellationToken)).IsOk);

        var entries = rig.Store.Snapshot(rig.Key)!.Entries;
        Assert.Contains(entries, e => e.Name == "Elsewhere" && e.IsDirectory);
        Assert.Contains(entries, e => e.Key == "N1" && e.Name == "minutes.txt");
        Assert.DoesNotContain(entries, e => e.Key == "R1");
        Assert.Contains("N1", seen);
        Assert.Contains("R1", seen);
    }

    [Fact]
    public async Task Refresh_reads_everything_again_when_the_server_cannot_say_what_changed()
    {
        using var rig = await FileRig.CreateAsync();
        _ = await rig.LoadAsync();
        _ = await rig.CallAsync("FileNode/set", new JsonObject { ["accountId"] = rig.Server.Options.AccountId, ["destroy"] = new JsonArray("R1") });
        rig.Server.Options.Files!.ExpireStates();

        Assert.True((await rig.Store.RefreshAsync(rig.Key, TestContext.Current.CancellationToken)).IsOk);
        Assert.DoesNotContain(rig.Store.Snapshot(rig.Key)!.Entries, e => e.Key == "R1");
    }

    [Fact]
    public async Task A_push_for_FileNode_refreshes_the_store()
    {
        using var rig = await FileRig.CreateAsync(new FakeFiles { SeedM5 = true });
        using var watching = new FileStore(rig, rig.Hooks, null, TimeProvider.System, watchPush: true);
        _ = await watching.LoadAsync(rig.Key, TestContext.Current.CancellationToken);
        for (var i = 0; i < 300 && rig.Server.Push.OpenCount == 0; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(rig.Server.Push.OpenCount > 0);
        _ = await rig.CallAsync("FileNode/set", new JsonObject { ["accountId"] = rig.Server.Options.AccountId, ["destroy"] = new JsonArray("R1") });
        rig.Server.Push.Send("state", """{"@type":"StateChange","changed":{"b":{"FileNode":"f9"}}}""");

        for (var i = 0; i < 300 && watching.Snapshot(rig.Key)!.Entries.Any(e => e.Key == "R1"); i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.DoesNotContain(watching.Snapshot(rig.Key)!.Entries, e => e.Key == "R1");
    }

    [Fact]
    public async Task Favourites_and_recent_are_kept_on_this_device_in_files_json()
    {
        using var rig = await FileRig.CreateAsync();
        _ = await rig.LoadAsync();
        rig.Store.ToggleStar("N1");
        rig.Store.ToggleStar("I1");
        rig.Store.ToggleStar("N1");
        for (var i = 0; i < 40; i++)
        {
            rig.Store.AddRecent("k" + i);
        }

        rig.Store.AddRecent("k5");
        rig.Store.RemoveRecent("k39");
        await rig.Store.DrainAsync();

        Assert.Equal(["I1"], rig.Store.Favourites);
        Assert.Equal(29, rig.Store.Recent.Count);
        Assert.Equal("k5", rig.Store.Recent[0]);
        Assert.DoesNotContain("k39", rig.Store.Recent);

        // a second store over the same profile reads them back (Rust's keys, so a Rust profile carries over)
        using var again = new FileStore(rig, rig.Hooks, new Barbican.Core.Tests.Settings.Prefs.TestProfilePaths(rig.Root), watchPush: false);
        Assert.Equal(["I1"], again.Favourites);
        Assert.Equal("k5", again.Recent[0]);
        var text = File.ReadAllText(Path.Combine(rig.Root, "config", "files.json"));
        Assert.Contains("filesFavorites", text, StringComparison.Ordinal);
        Assert.Contains("filesRecent", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Starring_tells_the_plugins()
    {
        using var rig = await FileRig.CreateAsync();
        _ = await rig.LoadAsync();
        rig.Store.ToggleStar("N1");

        var payload = Assert.IsType<Barbican.Core.Plugins.Payloads.FileFavouritePayload>(await rig.HookAsync("onFileFavoriteToggle"));
        Assert.True(payload.Favorite);
        Assert.Equal("notes.txt", payload.File.Name);
        Assert.Equal("/Documents", payload.File.Path);
    }
}
