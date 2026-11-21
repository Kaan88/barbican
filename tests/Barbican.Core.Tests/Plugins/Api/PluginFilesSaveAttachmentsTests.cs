// Source: Bulwark lib/plugin-sandbox/assistant/d.ts:55,73,506-546 (files.saveAttachments: files:write and email:read, 1 to 20 blob ids that are attachments of the message, a folder of the user's own drive, a taken name becomes "name (2).ext",
// 20 writes a minute counted before anything is read, resolves to { saved, stopped }) and lib/files-save.ts (filed by blobId, onBeforeFileUpload may stop a file, onAfterFileUpload reports each). The real dispatcher over the real
// file store, the real blob saver and hook bus and the real mail service on the fake JMAP server; Barbican fences: denial reaches no server, a stale call before the write writes nothing, one during it is an unknown outcome.
#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Files;
using Barbican.Core.Plugins.Api.Files;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Tests.Settings.Prefs;
using Barbican.FakeJmap;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginFilesSaveAttachmentsTests
{
    private static readonly string[] InfoFields = ["name", "kind", "path", "parentId", "mimeType"];

    private sealed class Readers : IPluginMailReaders
    {
        public Barbican.Core.Mail.IMailService Reader(IAccountSession session, string? sharedAccountId) => sharedAccountId is null ? session.Mail : throw new NotSupportedException();
    }

    private sealed class Rig : IAccountManager, IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "barbican-save-" + Guid.NewGuid().ToString("N"));
        private PluginApiRig.Session? _session;

        public PluginApiRig Api { get; private set; } = null!;

        public FakeFiles Files { get; } = new() { SeedM5 = true, SharedAccounts = ["s1"] };

        public HookBus Hooks { get; } = new(new FakeTimeProvider());

        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));

        public FileStore Store { get; private set; } = null!;

        public List<FileResourceView> Uploaded { get; } = [];

        public string MailId { get; private set; } = "";

        public string[] Blobs { get; private set; } = [];

        public string FilesAccount => FileAccounts.Primary(Api.F.Client.Session);

        private PluginApiRig.Session Login => _session ??= new PluginApiRig.Session(Api.F, () => null);

        public IReadOnlyList<AccountInfo> Accounts => [Login.Account];

        public AccountInfo? Active => Login.Account;

        public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged
        {
            add { }
            remove { }
        }

        public static async Task<Rig> StartAsync(params string[] permissions)
        {
            var rig = new Rig();
            _ = Directory.CreateDirectory(rig._root);
            rig.Api = await PluginApiRig.StartAsync(() =>
            {
                rig.Store = new FileStore(rig, rig.Hooks, new TestProfilePaths(rig._root), rig.Time, watchPush: false);
                var writer = new FileWriter(rig, rig.Store, rig.Hooks, rig.Time);
                var transfer = new FileTransfer(rig, rig.Store, rig.Hooks, null, rig.Time);
                var host = new PluginFilesHost(rig.Store, writer, transfer, new PluginMailRateLimiter(rig.Time));
                return [PluginFilesMethods.SaveAttachments(host, new Readers(), new FileBlobSaver(rig, rig.Store, rig.Hooks, rig.Time))];
            }, permissions, new FakeJmapOptions { FileNodeCapability = true, Files = rig.Files });
            var store = rig.Api.F.Server.Store;
            rig.MailId = store.Deliver(new FakeMail
            {
                Subject = "With files", Text = "see files",
                Attachments = [new FakeAttachment("note.txt", "text/plain", Encoding.UTF8.GetBytes("note")), new FakeAttachment("scan.pdf", "application/pdf", [1, 2, 3])],
            });
            rig.Blobs = [.. store.Email(rig.MailId)!["attachments"]!.AsArray().Select(a => (string)a!["blobId"]!)];
            _ = rig.Hooks.Observe<FileResourceView>(HookNames.OnAfterFileUpload, (view, _) =>
            {
                rig.Uploaded.Add(view);
                return ValueTask.CompletedTask;
            });
            return rig;
        }

        public Task<PluginCallOutcome> CallAsync(string args) => Api.CallAsync("files.saveAttachments", args);

        public JsonObject? Node(string name) => Files.Nodes(FilesAccount).FirstOrDefault(n => (string?)n["name"] == name);

        public IEnumerable<string> Names() => Files.Nodes(FilesAccount).Select(n => (string)n["name"]!);

        public string Args(string blobs, string? folder = null, string? options = null) => $"[\"{MailId}\",{blobs}{(folder is null ? "" : "," + folder)}{(options is null ? "" : (folder is null ? ",null," : ",") + options)}]";

        public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();

        public IAccountSession? SessionFor(string accountKey) => string.Equals(accountKey, Login.Account.Key, StringComparison.Ordinal) ? Login : null;

        public void Dispose()
        {
            Store.Dispose();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // a scratch folder
            }
        }
    }

    // the observers are told in the background
    private static async Task WaitAsync(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Saved_attachments_become_files_by_blob_id_in_the_chosen_folder_run_the_hooks_and_never_overwrite()
    {
        using var rig = await Rig.StartAsync("files:write", "email:read");
        var result = await rig.CallAsync(rig.Args($"[\"{rig.Blobs[0]}\"]", "\"D1\""));
        Assert.True(result.IsSuccess);
        Assert.Equal(["saved", "stopped"], PluginApiRig.Keys(result.Value!), StringComparer.Ordinal);
        var saved = result.Value!.Property("saved");
        Assert.Equal(1, saved.Count);
        Assert.Equal(["id", "kind", "mimeType", "modified", "name", "parentId", "path", "size"], PluginApiRig.Keys(saved.Item(0)), StringComparer.Ordinal);
        Assert.Equal(["note.txt", "file", "/Documents", "D1", "text/plain"], InfoFields.Select(n => saved.Item(0).Property(n).StringValue), StringComparer.Ordinal);
        Assert.Equal(0, result.Value!.Property("stopped").Count);
        // by blob id: the node points at the attachment's own blob (nothing was downloaded or uploaded)
        var node = rig.Node("note.txt")!;
        Assert.Equal(rig.Blobs[0], (string?)node["blobId"], StringComparer.Ordinal);
        Assert.Equal("D1", (string?)node["parentId"], StringComparer.Ordinal);
        await WaitAsync(() => rig.Uploaded.Any(u => u.Name == "note.txt" && u.Path == "/Documents"));
        Assert.Contains(rig.Uploaded, u => u.Name == "note.txt" && u.Path == "/Documents");
        // again: a taken name becomes "note (2).txt", the first stays; two blobs at once, a repeated id counts once
        var again = (await rig.CallAsync(rig.Args($"[\"{rig.Blobs[0]}\",\"{rig.Blobs[1]}\",\"{rig.Blobs[0]}\"]", "\"D1\""))).Value!;
        Assert.Equal(["note (2).txt", "scan.pdf"], Enumerable.Range(0, again.Property("saved").Count).Select(i => again.Property("saved").Item(i).Property("name").StringValue), StringComparer.Ordinal);
        Assert.Equal(1, rig.Names().Count(n => n == "note.txt"));
        // the top of the drive when no folder is given
        var top = (await rig.CallAsync(rig.Args($"[\"{rig.Blobs[1]}\"]"))).Value!;
        Assert.Equal("/", top.Property("saved").Item(0).Property("path").StringValue, StringComparer.Ordinal);
        Assert.Equal(PluginValueKind.Null, top.Property("saved").Item(0).Property("parentId").Kind);
    }

    [Fact]
    public async Task An_add_on_that_stops_the_upload_hook_makes_the_file_stopped_and_no_node_appears()
    {
        using var rig = await Rig.StartAsync("files:write", "email:read");
        using var stop = rig.Hooks.Intercept<FileUploadPayload>(HookNames.OnBeforeFileUpload, (payload, _) => ValueTask.FromResult(!string.Equals(payload.Name, "note.txt", StringComparison.Ordinal)));
        var result = (await rig.CallAsync(rig.Args($"[\"{rig.Blobs[0]}\",\"{rig.Blobs[1]}\"]", "\"D1\""))).Value!;
        Assert.Equal(["note.txt"], Enumerable.Range(0, result.Property("stopped").Count).Select(i => result.Property("stopped").Item(i).StringValue), StringComparer.Ordinal);
        Assert.Equal(["scan.pdf"], Enumerable.Range(0, result.Property("saved").Count).Select(i => result.Property("saved").Item(i).Property("name").StringValue), StringComparer.Ordinal);
        Assert.Null(rig.Node("note.txt"));
    }

    [Theory]
    [InlineData("files:write")]
    [InlineData("email:read")]
    [InlineData("files:read", "email:blob-read")]
    public async Task It_needs_both_files_write_and_email_read_and_a_denied_call_reaches_no_server(params string[] held)
    {
        using var rig = await Rig.StartAsync(held);
        var calls = rig.Api.F.Server.ApiCalls.Count;
        var names = rig.Names().ToList();
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync(rig.Args($"[\"{rig.Blobs[0]}\"]"))).Error);
        Assert.Equal(calls, rig.Api.F.Server.ApiCalls.Count);
        Assert.Equal(names, rig.Names().ToList());
        Assert.Empty(rig.Uploaded);
    }

    [Fact]
    public async Task Arguments_are_checked_before_anything_is_read_and_unusable_targets_are_refused()
    {
        using var rig = await Rig.StartAsync("files:write", "email:read");
        var calls = rig.Api.F.Server.ApiCalls.Count;
        var ids21 = "[" + string.Join(",", Enumerable.Range(0, 21).Select(i => $"\"b{i}\"")) + "]";
        string[] bad =
        [
            "[]", "[5]", $"[\"{rig.MailId}\"]", $"[\"{rig.MailId}\",[]]", $"[\"{rig.MailId}\",\"x\"]", $"[\"{rig.MailId}\",[5]]", $"[\"{rig.MailId}\",[\"\"]]", $"[\"{rig.MailId}\",{ids21}]", $"[\"\",[\"b\"]]",
            rig.Args("[\"b\"]", "5"), rig.Args("[\"b\"]", "null", "{\"other\":1}"), rig.Args("[\"b\"]", "null", "{\"accountId\":5}"), rig.Args("[\"b\"]", "null", "5"),
        ];
        foreach (var args in bad) Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync(args)).Error);
        Assert.Equal(calls, rig.Api.F.Server.ApiCalls.Count);
        // a file instead of a folder, the Files trash, another person's drive
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync(rig.Args($"[\"{rig.Blobs[0]}\"]", "\"N1\""))).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync(rig.Args($"[\"{rig.Blobs[0]}\"]", "\"D4\""))).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync(rig.Args($"[\"{rig.Blobs[0]}\"]", "\"s1|S1\""))).Error);
        Assert.Null(rig.Node("note.txt"));
        // a blob that is not an attachment of the message, a message that is not there, an account the login does not show
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync(rig.Args("[\"B9\"]"))).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync($"[\"ghost\",[\"{rig.Blobs[0]}\"]]")).Error);
        var before = rig.Api.F.Server.ApiCalls.Count;
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync(rig.Args($"[\"{rig.Blobs[0]}\"]", "null", "{\"accountId\":\"stranger\"}"))).Error);
        Assert.Equal(before, rig.Api.F.Server.ApiCalls.Count);
        Assert.Null(rig.Node("note.txt"));
        Assert.Empty(rig.Uploaded);
    }

    [Fact]
    public async Task Twenty_writes_a_minute_count_before_anything_is_read_even_when_refused()
    {
        using var rig = await Rig.StartAsync("files:write", "email:read");
        for (var i = 0; i < 20; i++) Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync(rig.Args("[\"B9\"]"))).Error);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync(rig.Args($"[\"{rig.Blobs[0]}\"]"))).Error);
        Assert.Null(rig.Node("note.txt"));
        rig.Time.Advance(TimeSpan.FromSeconds(61));
        Assert.True((await rig.CallAsync(rig.Args($"[\"{rig.Blobs[0]}\"]"))).IsSuccess);
    }

    [Fact]
    public async Task A_call_that_goes_stale_before_the_write_saves_nothing_and_one_that_goes_stale_during_it_is_an_unknown_outcome()
    {
        using var rig = await Rig.StartAsync("files:write", "email:read");
        rig.Api.Active.Valid = false;
        Assert.Equal(PluginErrorCode.NoSession, (await rig.CallAsync(rig.Args($"[\"{rig.Blobs[0]}\"]"))).Error);
        rig.Api.Active.Valid = true;
        // the session goes while the message is read: nothing was issued
        rig.Api.OnMailCall = () => rig.Api.Active.Valid = false;
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync(rig.Args($"[\"{rig.Blobs[0]}\"]"))).Error);
        rig.Api.OnMailCall = null;
        rig.Api.Active.Valid = true;
        Assert.Null(rig.Node("note.txt"));
        Assert.Empty(rig.Uploaded);
        // the add-on is revoked while the file is made: the write happened, the answer is withheld
        using var revoke = rig.Hooks.Intercept<FileUploadPayload>(HookNames.OnBeforeFileUpload, (_, _) =>
        {
            rig.Api.Auth.Current = PluginApiRig.Grant(["files:write", "email:read"], enabled: false);
            return ValueTask.FromResult(true);
        });
        Assert.Equal(PluginErrorCode.UnknownOutcome, (await rig.CallAsync(rig.Args($"[\"{rig.Blobs[0]}\"]"))).Error);
        Assert.NotNull(rig.Node("note.txt"));
    }
}
