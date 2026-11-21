// Source: Bulwark lib/plugin-sandbox/assistant/d.ts:56-59,411-675 (files.create, files.createFolder, files.move, files.rename: files:write, writes only into the user's own drive outside the trash and never in a shared folder, names the
// server takes, 20 changes a minute, previousFolderId and previousName for an undo), d.types.ts:70-95. The real dispatcher over the real file store, writer, transfer service and hook bus on the fake JMAP server.
#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Protocol;
using Barbican.FakeJmap;
using Rig = Barbican.Core.Tests.Plugins.Api.PluginFilesRig;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginFilesWriteTests
{
    private static readonly string[] InfoKeys = ["id", "kind", "mimeType", "modified", "name", "parentId", "path", "size"];

    private static FakeFiles Seed(bool rules = false)
    {
        var files = new FakeFiles { SeedM5 = true, SharedAccounts = ["s1"], NameRules = rules };
        _ = files.Add("b", null, "Outbox", true, id: "SO", shareWith: new JsonObject { ["u1"] = new JsonObject { ["mayRead"] = true } });
        _ = files.Add("b", "SO", "in.txt", false, id: "SO1");
        _ = files.Add("b", "SO", "Deep", true, id: "SO2");
        _ = files.Add("b", "D3", "notes.txt", false, id: "PN");
        return files;
    }

    private static JsonObject Node(FakeFiles files, string id) => files.Nodes("b").First(n => (string?)n["id"] == id);

    private static string? ParentOf(FakeFiles files, string id) => (string?)Node(files, id)["parentId"];

    [Fact]
    public async Task A_created_text_file_lands_in_the_own_drive_runs_the_upload_hook_and_never_overwrites()
    {
        var files = Seed();
        using var rig = await Rig.StartAsync(files: files);
        var made = (await rig.CallAsync("files.create", """[{"folderId":"D1","name":" todo.md ","content":"# Hello","type":"text/markdown"}]""")).Value!;
        Assert.Equal(["file"], Rig.Keys(made), StringComparer.Ordinal);
        var info = made.Property("file");
        Assert.Equal(InfoKeys, Rig.Keys(info), StringComparer.Ordinal);
        Assert.Equal(["todo.md", "file", "/Documents", "D1", "text/markdown", "7"], Rig.Pick(info, "name", "kind", "path", "parentId", "mimeType", "size"), StringComparer.Ordinal);
        Assert.Equal("todo.md", (string?)Node(files, info.Property("id").StringValue)["name"]);
        Assert.Contains(HookNames.OnBeforeFileUpload, rig.Files.Hooks.Seen.Select(s => s.Hook));
        var clash = (await rig.CallAsync("files.create", """[{"name":"readme.md","content":"again","type":"text/markdown"}]""")).Value!;
        Assert.Equal("readme (2).md", clash.Property("file").Property("name").StringValue, StringComparer.Ordinal);
        Assert.Equal("/", clash.Property("file").Property("path").StringValue, StringComparer.Ordinal);
        Assert.Equal("# Readme\n", files.Nodes("b").Count(n => (string?)n["name"] == "readme.md") == 1 ? "# Readme\n" : "overwritten");
        var plain = (await rig.CallAsync("files.create", """[{"folderId":null,"name":"log","content":"x","type":"text/plain"}]""")).Value!;
        Assert.Equal("text/plain", plain.Property("file").Property("mimeType").StringValue, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Create_refuses_bad_arguments_unusable_places_replace_and_a_stopped_upload_without_a_node_appearing()
    {
        var files = Seed(rules: true);
        using var rig = await Rig.StartAsync(files: files);
        string[] bad =
        [
            "[]", "[1]", """[{"content":"x","type":"text/plain"}]""", """[{"name":"a.txt","type":"text/plain"}]""", """[{"name":"a.txt","content":"x"}]""", """[{"name":"a.txt","content":"x","type":"text/html"}]""",
            """[{"name":"a.txt","content":"x","type":"text/plain","other":1}]""", """[{"name":"a/b.txt","content":"x","type":"text/plain"}]""", """[{"name":"..","content":"x","type":"text/plain"}]""",
            """[{"name":".Trash","content":"x","type":"text/plain"}]""", "[{\"name\":\"a\\u0001b\",\"content\":\"x\",\"type\":\"text/plain\"}]", """[{"name":"   ","content":"x","type":"text/plain"}]""",
            """[{"name":"a.txt","content":5,"type":"text/plain"}]""", """[{"name":"a.txt","content":"x","type":"text/plain","folderId":5}]""", """[{"name":"a.txt","content":"x","type":"text/plain","replace":5}]""",
            "[{\"name\":\"a.txt\",\"content\":\"" + new string('x', 512 * 1024 + 1) + "\",\"type\":\"text/plain\"}]",
        ];
        foreach (var args in bad) Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.create", args)).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.create", """[{"name":"a:b.txt","content":"x","type":"text/plain"}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.create", """[{"folderId":"N1","name":"a.txt","content":"x","type":"text/plain"}]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.create", """[{"folderId":"D4","name":"a.txt","content":"x","type":"text/plain"}]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.create", """[{"folderId":"s1|S1","name":"a.txt","content":"x","type":"text/plain"}]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("files.create", """[{"folderId":"SO","name":"a.txt","content":"x","type":"text/plain"}]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("files.create", """[{"folderId":"SO2","name":"a.txt","content":"x","type":"text/plain"}]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.create", """[{"replace":"N1","content":"new","type":"text/plain"}]""")).Error);
        Assert.Equal(0, rig.Spy.Calls);
        rig.Files.Hooks.Deny(HookNames.OnBeforeFileUpload);
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("files.create", """[{"name":"stopped.txt","content":"x","type":"text/plain"}]""")).Error);
        Assert.DoesNotContain(files.Nodes("b"), n => (string?)n["name"] == "stopped.txt");
    }

    [Fact]
    public async Task A_created_folder_gets_a_unique_name_runs_its_hook_and_stays_out_of_shared_places()
    {
        var files = Seed();
        using var rig = await Rig.StartAsync(files: files);
        var made = (await rig.CallAsync("files.createFolder", """[{"parentId":"D1","name":"Reports"}]""")).Value!;
        Assert.Equal(InfoKeys, Rig.Keys(made), StringComparer.Ordinal);
        Assert.Equal(["Reports", "folder", "/Documents", "D1"], Rig.Pick(made, "name", "kind", "path", "parentId"), StringComparer.Ordinal);
        Assert.Equal("D1", ParentOf(files, made.Property("id").StringValue));
        _ = await rig.Files.HookAsync(HookNames.OnDirectoryCreate);
        Assert.Equal("Invoices (2)", (await rig.CallAsync("files.createFolder", """[{"parentId":"D1","name":"Invoices"}]""")).Value!.Property("name").StringValue, StringComparer.Ordinal);
        Assert.Equal(PluginValueKind.Null, (await rig.CallAsync("files.createFolder", """[{"name":"Top"}]""")).Value!.Property("parentId").Kind);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("files.createFolder", """[{"parentId":"SO","name":"x"}]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("files.createFolder", """[{"parentId":"SO2","name":"x"}]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.createFolder", """[{"parentId":"s1|S1","name":"x"}]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.createFolder", """[{"parentId":"D4","name":"x"}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.createFolder", """[{"parentId":"N1","name":"x"}]""")).Error);
        foreach (var bad in new[] { "[]", """[{}]""", """[{"name":"a/b"}]""", """[{"name":"x","other":1}]""", """[{"name":5}]""", """[{"name":"x","parentId":5}]""" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.createFolder", bad)).Error);
        }
    }

    [Fact]
    public async Task A_move_goes_through_the_writer_reports_where_it_came_from_and_keeps_to_the_own_drive()
    {
        var files = Seed();
        using var rig = await Rig.StartAsync(files: files);
        var moved = (await rig.CallAsync("files.move", """["I1","D3"]""")).Value!;
        Assert.Equal(["file", "previousFolderId"], Rig.Keys(moved), StringComparer.Ordinal);
        Assert.Equal("D2", moved.Property("previousFolderId").StringValue, StringComparer.Ordinal);
        Assert.Equal(["march.txt", "/Photos", "D3"], Rig.Pick(moved.Property("file"), "name", "path", "parentId"), StringComparer.Ordinal);
        Assert.Equal("D3", ParentOf(files, "I1"));
        Assert.Contains(HookNames.OnBeforeFileMove, rig.Files.Hooks.Seen.Select(s => s.Hook));
        var back = await rig.CallAsync("files.move", """["I1",null]""");
        Assert.Equal(PluginValueKind.Null, back.Value!.Property("file").Property("parentId").Kind);
        Assert.Equal("D3", back.Value.Property("previousFolderId").StringValue, StringComparer.Ordinal);
        Assert.Null(ParentOf(files, "I1"));
        Assert.Equal(PluginErrorCode.Conflict, (await rig.CallAsync("files.move", """["I1",null]""")).Error);
    }

    [Fact]
    public async Task A_move_refuses_a_cycle_a_taken_name_shared_and_trashed_places()
    {
        var files = Seed();
        using var rig = await Rig.StartAsync(files: files);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.move", """["D1","D2"]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.move", """["D1","D1"]""")).Error);
        Assert.Equal(PluginErrorCode.Conflict, (await rig.CallAsync("files.move", """["PN","D1"]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("files.move", """["N1","s1|S1"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.move", """["s1|S3","D1"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.move", """["T3","D1"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.move", """["N1","D4"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.move", """["nope","D1"]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("files.move", """["SO1","D1"]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("files.move", """["N1","SO2"]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.move", """["N1","R1"]""")).Error);
        foreach (var bad in new[] { "[]", "[5]", """["N1",5]""", """[5,"D1"]""" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.move", bad)).Error);
        }

        Assert.Equal(0, rig.Spy.Calls);
        Assert.Equal("D1", ParentOf(files, "N1"));
    }

    [Fact]
    public async Task A_rename_reports_the_previous_name_and_refuses_the_same_name_a_taken_one_and_places_it_may_not_touch()
    {
        var files = Seed(rules: true);
        using var rig = await Rig.StartAsync(files: files);
        var renamed = (await rig.CallAsync("files.rename", """["N1"," minutes.txt "]""")).Value!;
        Assert.Equal(["file", "previousName"], Rig.Keys(renamed), StringComparer.Ordinal);
        Assert.Equal(["notes.txt", "minutes.txt", "/Documents"], [renamed.Property("previousName").StringValue, renamed.Property("file").Property("name").StringValue, renamed.Property("file").Property("path").StringValue], StringComparer.Ordinal);
        Assert.Equal("minutes.txt", (string?)Node(files, "N1")["name"]);
        Assert.Contains(HookNames.OnBeforeFileRename, rig.Files.Hooks.Seen.Select(s => s.Hook));
        _ = await rig.Files.HookAsync(HookNames.OnFileRename);
        Assert.Equal(PluginErrorCode.Conflict, (await rig.CallAsync("files.rename", """["N1","minutes.txt"]""")).Error);
        Assert.Equal(PluginErrorCode.Conflict, (await rig.CallAsync("files.rename", """["N1","Invoices"]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.rename", """["N1","a:b"]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.rename", """["N1","a/b"]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.rename", """["N1",".Trash"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.rename", """["s1|S3","x.txt"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.rename", """["T3","x.txt"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.rename", """["D4","x"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.rename", """["nope","x"]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("files.rename", """["SO1","x.txt"]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("files.rename", """["SO","x"]""")).Error);
        foreach (var bad in new[] { "[]", "[5]", """["N1"]""", """["N1",5]""", """[5,"x"]""" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.rename", bad)).Error);
        }
    }

    [Fact]
    public async Task A_stopped_move_or_rename_changes_nothing()
    {
        var files = Seed();
        using var rig = await Rig.StartAsync(files: files);
        rig.Files.Hooks.Deny(HookNames.OnBeforeFileMove);
        rig.Files.Hooks.Deny(HookNames.OnBeforeFileRename);
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("files.move", """["I1","D3"]""")).Error);
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("files.rename", """["N1","other.txt"]""")).Error);
        Assert.Equal("D2", ParentOf(files, "I1"));
        Assert.Equal("notes.txt", (string?)Node(files, "N1")["name"]);
    }

    [Fact]
    public async Task Changes_are_limited_to_twenty_a_minute_and_a_refused_one_counts()
    {
        using var rig = await Rig.StartAsync();
        for (var i = 0; i < 19; i++) Assert.True((await rig.CallAsync("files.createFolder", $$"""[{"name":"f{{i}}"}]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.rename", """["nope","x"]""")).Error);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("files.createFolder", """[{"name":"over"}]""")).Error);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("files.create", """[{"name":"a.txt","content":"x","type":"text/plain"}]""")).Error);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("files.move", """["N1","D3"]""")).Error);
        rig.Files.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.True((await rig.CallAsync("files.createFolder", """[{"name":"after"}]""")).IsSuccess);
    }

    [Fact]
    public async Task A_login_that_went_stale_before_a_write_stops_it_and_one_that_changed_during_it_reports_an_unknown_outcome()
    {
        var before = await Rig.StartAsync();
        using var _ = before;
        before.Store.OnSnapshot = () => before.Sessions.Valid = false;
        Assert.Equal(PluginErrorCode.StaleContext, (await before.CallAsync("files.createFolder", """[{"name":"x"}]""")).Error);
        before.Sessions.Valid = true;
        Assert.Equal(PluginErrorCode.StaleContext, (await before.CallAsync("files.rename", """["N1","y.txt"]""")).Error);
        before.Sessions.Valid = true;
        Assert.Equal(PluginErrorCode.StaleContext, (await before.CallAsync("files.create", """[{"name":"a.txt","content":"x","type":"text/plain"}]""")).Error);
        before.Sessions.Valid = true;
        Assert.Equal(PluginErrorCode.StaleContext, (await before.CallAsync("files.move", """["N1","D3"]""")).Error);
        Assert.Equal(0, before.Spy.Calls);

        using var during = await Rig.StartAsync();
        during.Spy.Before = () => during.Sessions.Valid = false;
        var result = await during.CallAsync("files.createFolder", """[{"name":"x"}]""");
        Assert.Equal(PluginErrorCode.UnknownOutcome, result.Error);
        Assert.Null(result.Value);

        using var revoked = await Rig.StartAsync();
        revoked.Spy.Before = () => revoked.Authority.Current = Rig.Grant(revoked.Files.Key, "files:read");
        Assert.Equal(PluginErrorCode.UnknownOutcome, (await revoked.CallAsync("files.rename", """["N1","z.txt"]""")).Error);
    }
}
