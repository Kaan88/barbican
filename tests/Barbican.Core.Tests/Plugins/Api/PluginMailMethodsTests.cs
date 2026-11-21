// Source: Bulwark lib/plugin-sandbox/host-api-mail.ts:86-181, assistant/b.ts:147-193, host-api.ts:156,334-337; lib/__tests__/plugin-host-thread.test.ts
// (permission, accounts, newest-50 thread, 60 and 120 reads a minute) over FakeJmap; Barbican fences (stale account, revoked add-on, delivery).
using System.Text.Json;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Tests.Jmap;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginMailMethodsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly string[] ReadKeys = ["accountId", "attachments", "cc", "from", "hasAttachment", "id", "isFlagged", "isRead", "keywords", "mailboxIds", "preview", "receivedAt", "source", "subject", "text", "threadId", "to"];
    private static readonly string[] SummaryKeys = ["accountId", "from", "hasAttachment", "id", "keywords", "mailboxIds", "preview", "receivedAt", "size", "subject", "threadId", "to"];

    private static PluginGrantSnapshot Grant(string permission = "email:read", bool enabled = true, long generation = 1) =>
        new(new PluginPackageIdentity("probe", "1.0.0", new string('a', 64)), generation, enabled, [permission], ["a1"]);

    private sealed class Rig
    {
        public Fake F = null!;
        public Authority Authority = null!;
        public Sessions Sessions = null!;
        public PluginApiDispatcher Dispatcher = null!;
        public Readers Readers = null!;
        public PluginRuntimeBinding Binding = null!;
        public FakeTimeProvider Time = new();

        public static async Task<Rig> StartAsync(PluginGrantSnapshot? grant = null)
        {
            var rig = new Rig { F = await Fake.StartAsync(time: Fake.VirtualClock()) };
            var session = new Session(rig.F);
            rig.Sessions = new Sessions(session);
            rig.Authority = new Authority(grant ?? Grant());
            rig.Readers = new Readers();
            rig.Dispatcher = new("auto-mail", PluginMailMethods.Create(rig.Readers, new PluginMailRateLimiter(rig.Time)), rig.Authority, rig.Sessions);
            rig.Binding = PluginRuntimeBinding.Create("auto-mail", rig.Authority.Current, PluginFrameKind.Background);
            return rig;
        }

        public Task<PluginCallOutcome> CallAsync(string method, string args) => CallAsync(Binding, method, args);

        public async Task<PluginCallOutcome> CallAsync(PluginRuntimeBinding binding, string method, string args)
        {
            using var doc = JsonDocument.Parse(args);
            return await Dispatcher.DispatchAsync(binding, new PluginApiRequest("r", method, PluginValue.FromJson(doc.RootElement)), Ct);
        }

        public string Inbox => F.Server.Store.MailboxId("inbox")!;

        public string IdOf(string subject, bool last = false)
        {
            var rows = F.Server.Store.Emails(F.Server.Store.EmailIds()).Where(e => string.Equals((string?)e["subject"], subject, StringComparison.Ordinal)).Select(e => (string)e["id"]!).ToList();
            return last ? rows[^1] : rows[0];
        }
    }

    private static List<string> Keys(PluginValue value) => [.. value.PropertyNames.Order(StringComparer.Ordinal)];

    [Fact]
    public async Task Get_returns_the_read_view_with_exactly_the_named_fields()
    {
        var rig = await Rig.StartAsync();
        var id = rig.IdOf("Report with attachments");
        var result = await rig.CallAsync("email.get", $$"""["{{id}}"]""");
        Assert.True(result.IsSuccess);
        var view = result.Value!;
        Assert.Equal(id, view.Property("id").StringValue, StringComparer.Ordinal);
        Assert.Equal("b", view.Property("accountId").StringValue, StringComparer.Ordinal);
        Assert.Empty(Keys(view).Except([.. ReadKeys, "headers"], StringComparer.Ordinal));
        Assert.Empty(ReadKeys.Except(Keys(view), StringComparer.Ordinal));
        Assert.False(view.Property("isRead").BooleanValue);
        Assert.Contains("report is attached", view.Property("text").StringValue, StringComparison.Ordinal);
        Assert.Contains("=== EMAIL BODY ===", view.Property("source").StringValue, StringComparison.Ordinal);
        var files = view.Property("attachments");
        Assert.Equal(2, files.Count);
        Assert.Empty(Keys(files.Item(0)).Except(["blobId", "name", "type", "size", "inline", "cid", "alt"], StringComparer.Ordinal));
        var inline = Enumerable.Range(0, files.Count).Select(files.Item).Single(a => a.Property("inline").BooleanValue);
        Assert.Equal("chart1@example.org", inline.Property("cid").StringValue, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Get_of_a_missing_message_or_an_unknown_account_is_null()
    {
        var rig = await Rig.StartAsync();
        Assert.Equal(PluginValueKind.Null, (await rig.CallAsync("email.get", """["no-such-id"]""")).Value!.Kind);
        var id = rig.IdOf("Flagged reminder");
        var before = rig.F.Server.ApiCalls.Count;
        Assert.Equal(PluginValueKind.Null, (await rig.CallAsync("email.get", $$"""["{{id}}","stranger"]""")).Value!.Kind);
        Assert.Equal(before, rig.F.Server.ApiCalls.Count);
        Assert.Equal(id, (await rig.CallAsync("email.get", $$"""["{{id}}","b"]""")).Value!.Property("id").StringValue, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("email.get", """["x"]""")]
    [InlineData("email.list", "[]")]
    [InlineData("email.search", """[{"text":"a"}]""")]
    [InlineData("email.getThread", """["x"]""")]
    [InlineData("email.getThreadCount", """["x"]""")]
    [InlineData("mailboxes.list", "[]")]
    [InlineData("jmap.getKeywords", "[]")]
    public async Task Every_method_is_denied_without_email_read_and_reaches_no_server(string method, string args)
    {
        var rig = await Rig.StartAsync(Grant("email:write"));
        var result = await rig.CallAsync(method, args);
        Assert.Equal(PluginErrorCode.PermissionDenied, result.Error);
        Assert.Empty(rig.F.Server.ApiCalls);
        Assert.Equal(0, rig.Readers.Calls);
    }

    [Fact]
    public async Task A_grant_that_does_not_name_the_active_login_gets_no_session()
    {
        var rig = await Rig.StartAsync(new PluginGrantSnapshot(new PluginPackageIdentity("probe", "1.0.0", new string('a', 64)), 1, true, ["email:read"], ["other"]));
        Assert.Equal(PluginErrorCode.NoSession, (await rig.CallAsync("mailboxes.list", "[]")).Error);
        Assert.Empty(rig.F.Server.ApiCalls);
    }

    [Fact]
    public async Task Mailboxes_list_projects_six_fields()
    {
        var rig = await Rig.StartAsync();
        var list = (await rig.CallAsync("mailboxes.list", "[]")).Value!;
        Assert.Equal(7, list.Count);
        var inbox = Enumerable.Range(0, list.Count).Select(list.Item).Single(m => m.Property("role").Kind == PluginValueKind.String && string.Equals(m.Property("role").StringValue, "inbox", StringComparison.Ordinal));
        Assert.Equal(["id", "name", "parentId", "role", "totalEmails", "unreadEmails"], Keys(inbox), StringComparer.Ordinal);
        Assert.Equal(9, double.Parse(inbox.Property("totalEmails").NumberText, System.Globalization.CultureInfo.InvariantCulture));
        var projects = Enumerable.Range(0, list.Count).Select(list.Item).Single(m => string.Equals(m.Property("name").StringValue, "Projects", StringComparison.Ordinal));
        Assert.Equal(PluginValueKind.Null, projects.Property("role").Kind);
        Assert.Equal(inbox.Property("id").StringValue, projects.Property("parentId").StringValue, StringComparer.Ordinal);
    }

    [Fact]
    public async Task List_pages_newest_first_with_a_position_and_bounds_the_limit()
    {
        var rig = await Rig.StartAsync();
        var first = (await rig.CallAsync("email.list", $$"""[{"mailboxId":"{{rig.Inbox}}","limit":4}]""")).Value!;
        var second = (await rig.CallAsync("email.list", $$"""[{"mailboxId":"{{rig.Inbox}}","limit":4,"position":4}]""")).Value!;
        Assert.Equal(4, first.Count);
        Assert.Equal(4, second.Count);
        Assert.Equal(SummaryKeys, Keys(first.Item(0)), StringComparer.Ordinal);
        var all = rig.F.Server.Store.Query(new System.Text.Json.Nodes.JsonObject { ["inMailbox"] = rig.Inbox });
        Assert.Equal(all.Take(8), Enumerable.Range(0, 4).Select(i => first.Item(i).Property("id").StringValue).Concat(Enumerable.Range(0, 4).Select(i => second.Item(i).Property("id").StringValue)), StringComparer.Ordinal);
        Assert.Equal(12, (await rig.CallAsync("email.list", "[]")).Value!.Count);
        foreach (var bad in new[] { """[{"limit":101}]""", """[{"limit":0}]""", """[{"limit":1.5}]""", """[{"position":-1}]""", """[{"position":1000001}]""", """[{"bogus":1}]""", "[{\"mailboxId\":\"\"}]", "[\"inbox\"]" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("email.list", bad)).Error);
        }
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("email.list", """[{"mailboxId":"nope"}]""")).Error);
        Assert.Equal(12, (await rig.CallAsync("email.list", """[{"limit":100}]""")).Value!.Count);
    }

    [Fact]
    public async Task Search_filters_by_named_fields_only_and_requires_a_condition()
    {
        var rig = await Rig.StartAsync();
        var bySubject = (await rig.CallAsync("email.search", """[{"subject":"quarterly"}]""")).Value!;
        Assert.Equal(5, bySubject.Count);
        Assert.Equal(SummaryKeys, Keys(bySubject.Item(0)), StringComparer.Ordinal);
        Assert.Equal(1, (await rig.CallAsync("email.search", """[{"hasAttachment":true}]""")).Value!.Count);
        Assert.Equal(2, (await rig.CallAsync("email.search", """[{"subject":"quarterly","limit":2}]""")).Value!.Count);
        Assert.Equal(3, (await rig.CallAsync("email.search", """[{"subject":"quarterly","position":2}]""")).Value!.Count);
        Assert.Equal(3, (await rig.CallAsync("email.search", $$"""[{"from":"example.org","after":"2026-11-01","before":"2026-11-02","mailboxId":"{{rig.Inbox}}"}]""")).Value!.Count);
        foreach (var bad in new[] { "[]", "[{}]", """[{"text":"   "}]""", """[{"limit":51,"text":"a"}]""", """[{"text":"a","filter":{}}]""", """[{"after":"not a date"}]""", """[{"text":1}]""", """[{"hasAttachment":"yes"}]""", """[{"text":"a","limit":0}]""" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("email.search", bad)).Error);
        }
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("email.search", """[{"text":"a","mailboxId":"nope"}]""")).Error);
    }

    [Fact]
    public async Task Thread_is_oldest_first_counted_apart_and_refuses_unknown_accounts()
    {
        var rig = await Rig.StartAsync();
        var id = rig.IdOf("Re: Quarterly planning");
        var thread = (await rig.CallAsync("email.getThread", $$"""["{{id}}"]""")).Value!;
        Assert.Equal(5, thread.Count);
        Assert.Equal("Message 1 of the planning thread.", thread.Item(0).Property("text").StringValue, StringComparer.Ordinal);
        Assert.Equal("Message 5 of the planning thread.", thread.Item(4).Property("text").StringValue, StringComparer.Ordinal);
        Assert.Empty(Keys(thread.Item(0)).Except([.. ReadKeys, "headers"], StringComparer.Ordinal));
        Assert.Equal(5, double.Parse((await rig.CallAsync("email.getThreadCount", $$"""["{{id}}",{"accountId":"b"}]""")).Value!.NumberText, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1, double.Parse((await rig.CallAsync("email.getThreadCount", $$"""["{{rig.IdOf("Flagged reminder")}}"]""")).Value!.NumberText, System.Globalization.CultureInfo.InvariantCulture));
        var calls = rig.F.Server.ApiCalls.Count;
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("email.getThread", $$"""["{{id}}",{"accountId":"stranger"}]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("email.getThreadCount", $$"""["{{id}}",{"accountId":"stranger"}]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("email.getThread", """["nope"]""")).Error);
        Assert.Equal(calls + 1, rig.F.Server.ApiCalls.Count);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("email.getThread", $$"""["{{id}}",{"threadId":"t"}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("email.getThread", "[42]")).Error);
    }

    [Fact]
    public async Task Thread_reads_allow_sixty_a_minute_and_counts_one_hundred_twenty_apart()
    {
        var rig = await Rig.StartAsync();
        var id = rig.IdOf("Flagged reminder");
        for (var i = 0; i < 60; i++) Assert.True((await rig.CallAsync("email.getThread", $$"""["{{id}}"]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("email.getThread", $$"""["{{id}}"]""")).Error);
        for (var i = 0; i < 120; i++) Assert.True((await rig.CallAsync("email.getThreadCount", $$"""["{{id}}"]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("email.getThreadCount", $$"""["{{id}}"]""")).Error);
        rig.Time.Advance(TimeSpan.FromMinutes(1.1));
        Assert.True((await rig.CallAsync("email.getThread", $$"""["{{id}}"]""")).IsSuccess);
    }

    [Fact]
    public async Task Keywords_count_what_is_set_without_the_server_keywords_and_validate_the_limit()
    {
        var rig = await Rig.StartAsync();
        var result = (await rig.CallAsync("jmap.getKeywords", "[]")).Value!;
        Assert.Equal(["complete", "keywords", "labels", "scanned", "total"], Keys(result), StringComparer.Ordinal);
        Assert.True(result.Property("complete").BooleanValue);
        Assert.Equal(12, double.Parse(result.Property("scanned").NumberText, System.Globalization.CultureInfo.InvariantCulture));
        var counts = result.Property("keywords");
        Assert.Equal(["$flagged", "$seen"], Keys(counts), StringComparer.Ordinal);
        Assert.Equal(6, double.Parse(counts.Property("$seen").NumberText, System.Globalization.CultureInfo.InvariantCulture));
        var label = result.Property("labels").Item(0);
        Assert.Equal(["color", "id", "isProviderLabel", "name", "source", "total", "unread"], Keys(label), StringComparer.Ordinal);
        var limited = (await rig.CallAsync("jmap.getKeywords", """[{"limit":3,"ignored":true}]""")).Value!;
        Assert.Equal(3, double.Parse(limited.Property("scanned").NumberText, System.Globalization.CultureInfo.InvariantCulture));
        Assert.False(limited.Property("complete").BooleanValue);
        foreach (var bad in new[] { """[{"limit":0}]""", """[{"limit":25001}]""", """[{"limit":"5"}]""", """["x"]""" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("jmap.getKeywords", bad)).Error);
        }
    }

    [Fact]
    public async Task An_account_change_during_the_read_drops_the_result()
    {
        var rig = await Rig.StartAsync();
        rig.Readers.OnRead = () => rig.Sessions.Valid = false;
        var result = await rig.CallAsync("email.get", $$"""["{{rig.IdOf("Flagged reminder")}}"]""");
        Assert.Equal(PluginErrorCode.StaleContext, result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task A_revoked_or_disabled_add_on_during_the_read_drops_the_result()
    {
        var rig = await Rig.StartAsync();
        rig.Readers.OnRead = () => rig.Authority.Current = Grant("email:read", enabled: false);
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("mailboxes.list", "[]")).Error);
        var changed = await Rig.StartAsync();
        changed.Readers.OnRead = () => changed.Authority.Current = Grant("email:write");
        Assert.Equal(PluginErrorCode.StaleContext, (await changed.CallAsync("email.list", "[]")).Error);
    }

    [Fact]
    public async Task A_generation_or_session_change_after_the_call_refuses_delivery()
    {
        var rig = await Rig.StartAsync();
        var result = await rig.CallAsync("mailboxes.list", "[]");
        Assert.True(result.IsSuccess);
        var wrote = 0;
        Assert.True(result.TryWriteForDelivery(rig.Binding, rig.Authority.Current, () => wrote++));
        Assert.Equal(1, wrote);
        rig.Sessions.Valid = false;
        Assert.False(result.TryWriteForDelivery(rig.Binding, rig.Authority.Current, () => wrote++));
        Assert.Equal(1, wrote);
        Assert.Equal(PluginErrorCode.StaleContext, result.RefuseDelivery().Error);
        rig.Sessions.Valid = true;
        Assert.False(result.TryWriteForDelivery(rig.Binding, Grant(generation: 2), () => wrote++));
        Assert.Equal(1, wrote);
    }

    private sealed class Readers : IPluginMailReaders
    {
        public int Calls { get; private set; }
        public Action? OnRead { get; set; }
        public IMailService Reader(IAccountSession session, string? sharedAccountId)
        {
            Calls++;
            OnRead?.Invoke();
            return sharedAccountId is null ? session.Mail : new MailService(session.Client, TimeProvider.System, sharedAccountId);
        }
    }

    private sealed class Authority(PluginGrantSnapshot initial) : IPluginAuthority
    {
        public PluginGrantSnapshot Current { get; set; } = initial;
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) => Task.FromResult(Result<PluginGrantSnapshot?>.Ok(Current));
    }

    private sealed class Sessions(IAccountSession session) : IPluginSessionSource
    {
        public bool Valid { get; set; } = true;
        public Result<PluginSessionCapture> CaptureActive() => Result<PluginSessionCapture>.Ok(new(session, 1));
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => Result<PluginSessionCapture>.Ok(new(session, null));
        public bool IsCurrent(PluginSessionCapture capture) => Valid && ReferenceEquals(capture.Session, session);
        public bool TryRunWhileCurrent(IReadOnlyList<PluginSessionCapture> captures, Func<bool> write) => Valid && captures.All(IsCurrent) && write();
    }

    private sealed class Session(Fake f) : IAccountSession
    {
        public AccountInfo Account { get; } = new("a1", "user@example.org", "https://jmap.example.org");
        public IJmapClient Client { get; } = f.Client;
        public IMailService Mail { get; } = new MailService(f.Client, f.Time);
        public IMailSync Sync => throw new NotSupportedException();
    }
}
