#pragma warning disable MA0006, MA0002 // assertions compare string literals

// Source: Bulwark lib/plugin-sandbox/assistant/d.ts:48-51,80-330 (files.list, files.find, files.getText, files.getImage: files:read, visible nodes without the Files trash, limits, rate budget), d.types.ts:30-68 (FileInfo, FileFindQuery),
// assistant/b.types.ts:44-93 (AttachmentTextView, AttachmentImageView), lib/image-shrink.ts. The real dispatcher over the real file store and transfer service on the fake JMAP server.
using SkiaSharp;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.FakeJmap;
using Rig = Barbican.Core.Tests.Plugins.Api.PluginFilesRig;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginFilesReadTests
{
    private static readonly string[] InfoKeys = ["id", "kind", "mimeType", "modified", "name", "parentId", "path", "size"];

    private static byte[] Png(int width, int height, bool transparent = false)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(transparent ? new SKColor(10, 20, 30, 0) : new SKColor(200, 30, 30));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task<Rig> StartWithAsync(Action<FakeFiles, Func<byte[], string>>? seed = null)
    {
        var files = new FakeFiles { SeedM5 = true, SharedAccounts = ["s1"] };
        var rig = await Rig.StartAsync(files: files);
        seed?.Invoke(files, rig.Files.Server.Store.PutBlob);
        return rig;
    }

    [Fact]
    public async Task List_gives_the_top_of_the_drive_and_of_what_was_shared_without_the_trash()
    {
        using var rig = await StartWithAsync();
        var top = (await rig.CallAsync("files.list", "[]")).Value!;
        Assert.Equal(["Documents", "Photos", "Team", "readme.md", "Shared notes.txt"], Rig.Names(top), StringComparer.Ordinal);
        Assert.Equal(InfoKeys, Rig.Keys(top.Item(0)), StringComparer.Ordinal);
        Assert.Equal(InfoKeys.Append("shared").Order(StringComparer.Ordinal), Rig.Keys(top.Item(2)), StringComparer.Ordinal);
        Assert.Equal(PluginValueKind.Null, top.Item(0).Property("parentId").Kind);
        Assert.Equal("", top.Item(0).Property("mimeType").StringValue, StringComparer.Ordinal);
        Assert.Equal("0", top.Item(0).Property("size").NumberText, StringComparer.Ordinal);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", top.Item(3).Property("modified").StringValue);
        Assert.Equal(top.Count, (await rig.CallAsync("files.list", "[null]")).Value!.Count);
        var documents = (await rig.CallAsync("files.list", """["D1"]""")).Value!;
        Assert.Equal(["Invoices", "notes.txt"], Rig.Names(documents), StringComparer.Ordinal);
        Assert.Equal("/Documents", documents.Item(1).Property("path").StringValue, StringComparer.Ordinal);
        Assert.Equal("D1", documents.Item(1).Property("parentId").StringValue, StringComparer.Ordinal);
        Assert.Equal("text/plain", documents.Item(1).Property("mimeType").StringValue, StringComparer.Ordinal);
        Assert.Equal(["plan.md"], Rig.Names((await rig.CallAsync("files.list", """["s1|S1"]""")).Value!));
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.list", """["D4"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.list", """["T1"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.list", """["nope"]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.list", """["N1"]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.list", "[5]")).Error);
    }

    [Fact]
    public async Task A_folder_lists_at_most_two_hundred_entries()
    {
        var files = new FakeFiles { SeedM5 = true, BulkNodes = 250 };
        using var rig = await Rig.StartAsync(files: files);
        Assert.Equal(200, (await rig.CallAsync("files.list", """["B0"]""")).Value!.Count);
    }

    [Fact]
    public async Task Find_filters_by_words_folder_kind_and_dates_and_never_looks_into_the_trash()
    {
        using var rig = await StartWithAsync();
        Assert.Equal(["Shared notes.txt", "notes.txt"], (await NamesAsync(rig, """[{"text":"NOTES"}]""")).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        Assert.Equal(["cat.png"], await NamesAsync(rig, """[{"kind":"image"}]"""));
        Assert.Equal(["Documents", "Invoices", "Photos", "Team"], (await NamesAsync(rig, """[{"kind":"folder"}]""")).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        Assert.Equal(["Invoices", "march.txt", "notes.txt"], (await NamesAsync(rig, """[{"folderId":"D1"}]""")).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        Assert.Equal(["Invoices", "march.txt"], (await NamesAsync(rig, """[{"text":"documents invoices"}]""")).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        Assert.Empty(await NamesAsync(rig, """[{"text":"old"}]"""));
        Assert.Empty(await NamesAsync(rig, """[{"text":"stray"}]"""));
        Assert.Empty(await NamesAsync(rig, """[{"modifiedAfter":"2100-01-01"}]"""));
        Assert.Equal(10, (await NamesAsync(rig, """[{"modifiedBefore":"2100-01-01T00:00:00Z","limit":50}]""")).Length);
        Assert.Single(await NamesAsync(rig, """[{"limit":1}]"""));
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.find", """[{"folderId":"nope"}]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.find", """[{"folderId":"D4"}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.find", """[{"folderId":"N1"}]""")).Error);
        foreach (var bad in new[] { """[{"kind":"video"}]""", """[{"limit":0}]""", """[{"limit":51}]""", """[{"modifiedAfter":"yesterday"}]""", """[{"modifiedAfter":""}]""", """[{"text":5}]""", """[{"other":1}]""", "[1]", """[{"text":"%s"}]""".Replace("%s", new string('x', 201))})
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.find", bad)).Error);
        }
    }

    private static async Task<string[]> NamesAsync(Rig rig, string args) => [.. Rig.Names((await rig.CallAsync("files.find", args)).Value!)];

    [Fact]
    public async Task Get_text_reads_a_text_file_cuts_it_and_names_the_formats_it_cannot_read()
    {
        using var rig = await StartWithAsync();
        var view = (await rig.CallAsync("files.getText", """["N1"]""")).Value!;
        Assert.Equal(["format", "name", "size", "status", "text", "truncated", "type"], Rig.Keys(view), StringComparer.Ordinal);
        Assert.Equal(["notes.txt", "text/plain", "text", "ok", "Meeting notes for the team.\n"], Rig.Pick(view, "name", "type", "format", "status", "text"), StringComparer.Ordinal);
        Assert.False(view.Property("truncated").BooleanValue);
        var cut = (await rig.CallAsync("files.getText", """["N1",{"maxChars":7}]""")).Value!;
        Assert.Equal("Meeting", cut.Property("text").StringValue, StringComparer.Ordinal);
        Assert.True(cut.Property("truncated").BooleanValue);
        var reads = rig.Spy.Reads;
        var image = (await rig.CallAsync("files.getText", """["P1"]""")).Value!;
        Assert.Equal(["other", "unsupported", ""], [image.Property("format").StringValue, image.Property("status").StringValue, image.Property("text").StringValue], StringComparer.Ordinal);
        Assert.Equal(reads, rig.Spy.Reads);
        Assert.Equal("shared\n", (await rig.CallAsync("files.getText", """["s1|S3"]""")).Value!.Property("text").StringValue, StringComparer.Ordinal);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.getText", """["T2"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("files.getText", """["nope"]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.getText", """["D1"]""")).Error);
        foreach (var bad in new[] { "[]", "[5]", """["N1",{"maxChars":0}]""", """["N1",{"maxChars":100001}]""", """["N1",{"maxChars":1.5}]""", """["N1",{"other":1}]""", """["N1",5]""" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.getText", bad)).Error);
        }
    }

    [Fact]
    public async Task A_file_over_twenty_megabytes_is_not_read_and_binary_content_is_not_text()
    {
        using var rig = await StartWithAsync((files, put) =>
        {
            _ = files.Add("b", null, "huge.txt", false, put([1]), "text/plain", 30_000_000, id: "H1");
            _ = files.Add("b", null, "binary.txt", false, put([65, 0, 66, 0, 67]), "text/plain", 5, id: "H2");
            _ = files.Add("b", null, "sheet.csv", false, put([0xEF, 0xBB, 0xBF, 0x61, 0x2C, 0x62, 0x0D, 0x0A, 0x31, 0x2C, 0xC3, 0xA9]), "text/csv", 12, id: "H3");
            _ = files.Add("b", null, "report.pdf", false, put([1]), "application/pdf", 1, id: "H4");
            _ = files.Add("b", null, "huge.png", false, put([1]), "image/png", 30_000_000, id: "H5");
        });
        var huge = (await rig.CallAsync("files.getText", """["H1"]""")).Value!;
        Assert.Equal(["too_big", "text", ""], [huge.Property("status").StringValue, huge.Property("format").StringValue, huge.Property("text").StringValue], StringComparer.Ordinal);
        Assert.Equal(0, rig.Spy.Reads);
        Assert.Equal("unsupported", (await rig.CallAsync("files.getText", """["H2"]""")).Value!.Property("status").StringValue, StringComparer.Ordinal);
        var csv = (await rig.CallAsync("files.getText", """["H3"]""")).Value!;
        Assert.Equal(["csv", "a,b\n1,é"], [csv.Property("format").StringValue, csv.Property("text").StringValue], StringComparer.Ordinal);
        var pdf = (await rig.CallAsync("files.getText", """["H4"]""")).Value!;
        Assert.Equal(["pdf", "unsupported"], [pdf.Property("format").StringValue, pdf.Property("status").StringValue], StringComparer.Ordinal);
        var readsBefore = rig.Spy.Reads;
        Assert.Equal("too_big", (await rig.CallAsync("files.getImage", """["H5"]""")).Value!.Property("status").StringValue, StringComparer.Ordinal);
        Assert.Equal(readsBefore, rig.Spy.Reads);
    }

    [Fact]
    public async Task Reads_of_content_are_limited_to_thirty_a_minute()
    {
        using var rig = await StartWithAsync();
        for (var i = 0; i < 30; i++) Assert.True((await rig.CallAsync("files.getText", """["N1"]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("files.getText", """["N1"]""")).Error);
        Assert.True((await rig.CallAsync("files.getImage", """["P1"]""")).IsSuccess);
        rig.Files.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.True((await rig.CallAsync("files.getText", """["N1"]""")).IsSuccess);
    }

    [Fact]
    public async Task Get_image_shrinks_a_picture_and_reports_the_statuses_of_the_ones_it_will_not_give()
    {
        using var rig = await StartWithAsync((files, put) =>
        {
            _ = files.Add("b", null, "big.png", false, put(Png(300, 200)), "image/png", 1, id: "G1");
            _ = files.Add("b", null, "clear.png", false, put(Png(300, 200, transparent: true)), "image/png", 1, id: "G2");
            _ = files.Add("b", null, "tiny.png", false, put(Png(50, 50)), "image/png", 1, id: "G3");
            _ = files.Add("b", null, "dot.png", false, put(Png(1, 1)), "image/png", 1, id: "G4");
            _ = files.Add("b", null, "logo.svg", false, put([60]), "image/svg+xml", 1, id: "G5");
            _ = files.Add("b", null, "broken.png", false, put([0x89, 0x50, 0x4E, 0x47, 1, 2, 3]), "image/png", 7, id: "G6");
        });
        var ok = (await rig.CallAsync("files.getImage", """["G1"]""")).Value!;
        Assert.Equal(["data", "height", "name", "status", "type", "width"], Rig.Keys(ok), StringComparer.Ordinal);
        Assert.Equal(["ok", "image/jpeg", "big.png"], [ok.Property("status").StringValue, ok.Property("type").StringValue, ok.Property("name").StringValue], StringComparer.Ordinal);
        Assert.Equal(["300", "200"], [ok.Property("width").NumberText, ok.Property("height").NumberText], StringComparer.Ordinal);
        Assert.Equal([0xFF, 0xD8], Convert.FromBase64String(ok.Property("data").StringValue).Take(2).Select(x => (int)x).ToArray());
        var small = (await rig.CallAsync("files.getImage", """["G1",{"maxSide":100,"exif":true}]""")).Value!;
        Assert.Equal(["100", "67"], [small.Property("width").NumberText, small.Property("height").NumberText], StringComparer.Ordinal);
        var clear = (await rig.CallAsync("files.getImage", """["G2"]""")).Value!;
        Assert.Equal(["ok", "image/png"], [clear.Property("status").StringValue, clear.Property("type").StringValue], StringComparer.Ordinal);
        var tiny = (await rig.CallAsync("files.getImage", """["G3"]""")).Value!;
        Assert.Equal(["tiny", "", "50"], [tiny.Property("status").StringValue, tiny.Property("data").StringValue, tiny.Property("width").NumberText], StringComparer.Ordinal);
        Assert.Equal(["tracker", "image/png"], [(await rig.CallAsync("files.getImage", """["G4"]""")).Value!.Property("status").StringValue, (await rig.CallAsync("files.getImage", """["G4"]""")).Value!.Property("type").StringValue], StringComparer.Ordinal);
        var reads = rig.Spy.Reads;
        Assert.Equal("unsupported", (await rig.CallAsync("files.getImage", """["G5"]""")).Value!.Property("status").StringValue, StringComparer.Ordinal);
        Assert.Equal("unsupported", (await rig.CallAsync("files.getImage", """["N1"]""")).Value!.Property("status").StringValue, StringComparer.Ordinal);
        Assert.Equal(reads, rig.Spy.Reads);
        Assert.Equal("unsupported", (await rig.CallAsync("files.getImage", """["G6"]""")).Value!.Property("status").StringValue, StringComparer.Ordinal);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.getImage", """["D1"]""")).Error);
        foreach (var bad in new[] { "[]", """["G1",{"maxSide":63}]""", """["G1",{"maxSide":2577}]""", """["G1",{"exif":"yes"}]""", """["G1",{"other":1}]""" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("files.getImage", bad)).Error);
        }
    }

    [Fact]
    public async Task A_neighbouring_permission_and_a_login_without_file_storage_are_refused_before_any_server_call()
    {
        using var writer = await Rig.StartAsync(["files:write"]);
        foreach (var (method, args) in new[] { ("files.list", "[]"), ("files.find", "[]"), ("files.getText", """["N1"]"""), ("files.getImage", """["P1"]""") })
        {
            Assert.Equal(PluginErrorCode.PermissionDenied, (await writer.CallAsync(method, args)).Error);
        }

        Assert.Empty(writer.Files.Server.ApiCalls);
        using var reader = await Rig.StartAsync(["files:read"]);
        foreach (var (method, args) in new[] { ("files.create", """[{"name":"a.md","content":"x","type":"text/markdown"}]"""), ("files.createFolder", """[{"name":"a"}]"""), ("files.move", """["N1",null]"""), ("files.rename", """["N1","b.txt"]""") })
        {
            Assert.Equal(PluginErrorCode.PermissionDenied, (await reader.CallAsync(method, args)).Error);
        }

        Assert.Equal(0, reader.Spy.Calls);
        Assert.Empty(reader.Files.Server.ApiCalls);
        using var none = await Rig.StartAsync(capability: false);
        Assert.Equal(PluginErrorCode.Unavailable, (await none.CallAsync("files.list", "[]")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await none.CallAsync("files.createFolder", """[{"name":"a"}]""")).Error);
        Assert.Equal(0, none.Spy.Calls);
    }

    [Fact]
    public async Task A_login_that_went_stale_during_a_read_drops_the_answer_and_a_disabled_add_on_gets_nothing()
    {
        using var rig = await Rig.StartAsync();
        rig.Store.OnSnapshot = () => rig.Sessions.Valid = false;
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("files.list", "[]")).Error);
        rig.Sessions.Valid = true;
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("files.find", "[]")).Error);
        rig.Store.OnSnapshot = null;
        rig.Sessions.Valid = true;
        rig.Authority.Current = new(rig.Authority.Current.Package, 2, false, ["files:read"], [rig.Files.Key]);
        Assert.False((await rig.CallAsync("files.list", "[]")).IsSuccess);
    }
}
