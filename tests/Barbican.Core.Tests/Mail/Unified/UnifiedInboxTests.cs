// New tests (M2 plan 6.5 exit tests) on FakeJmap with two logins and a shared account: the unified inbox merges by time with per-source
// paging, a source that fails does not empty the list, a new mail comes back as one added row (a delta, no reset), and an action on a
// unified row writes only to that row's account (each server's request log). Rust unified.rs:361 fetch, :474 shared_apply, mail.rs
// on_unified_push. Owner unified.

using Barbican.Core.Jmap;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Unified;
using Barbican.Core.Mail.Write;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Mail.Unified;

public sealed class UnifiedInboxTests : IDisposable
{
    private const string LoginA = "https://a.example.org\nana@example.org";
    private const string LoginB = "https://b.example.net\nben@example.net";
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeJmapServer _a = new(new FakeJmapOptions { Origin = "https://a.example.org", Username = "ana@example.org", AccountId = "a1", SharedAccounts = ["grp"] }, seed: false);
    private readonly FakeJmapServer _b = new(new FakeJmapOptions { Origin = "https://b.example.net", Username = "ben@example.net", AccountId = "b1" }, seed: false);
    private readonly string _inboxA;
    private readonly string _inboxB;

    public UnifiedInboxTests()
    {
        _inboxA = _a.Store.AddMailbox("Inbox", "inbox");
        _inboxB = _b.Store.AddMailbox("Inbox", "inbox");
        _a.Store.AddMailbox("Archive", "archive");
        _b.Store.AddMailbox("Projects", null);
        _b.Store.AddMailbox("Old mail", "archive");
        foreach (var h in new[] { 1, 3, 5 })
        {
            _a.Store.Deliver(new FakeMail { Subject = $"a{h}", ReceivedAt = T0.AddHours(h) }, _inboxA);
        }

        foreach (var h in new[] { 2, 4 })
        {
            _b.Store.Deliver(new FakeMail { Subject = $"b{h}", ReceivedAt = T0.AddHours(h) }, _inboxB);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _a.Dispose();
        _b.Dispose();
    }

    private async Task<(IJmapClient A, IJmapClient B)> ConnectAsync() =>
        (await FakeJmapHarness.ConnectAsync(_a, TimeProvider.System, ct: Ct), await FakeJmapHarness.ConnectAsync(_b, TimeProvider.System, ct: Ct));

    private static UnifiedListSource SourceOf(IJmapClient a, IJmapClient b, bool group = true)
    {
        var sources = UnifiedSources.Build([(LoginA, a), (LoginB, b)], LoginA, new UnifiedSettings(true, CrossAccount: true, IncludeGroup: group));
        return new UnifiedListSource(() => sources);
    }

    private static List<string> Subjects(IEnumerable<EmailSummary> rows) => [.. rows.Select(r => r.Subject ?? "")];

    [Fact]
    public async Task The_sources_are_the_logins_and_the_shared_account_and_the_active_login_comes_first()
    {
        var (a, b) = await ConnectAsync();
        var sources = UnifiedSources.Build([(LoginB, b), (LoginA, a)], LoginA, new UnifiedSettings(true, true, true));
        Assert.Equal([LoginA, $"{LoginA}#grp", LoginB], sources.Select(s => s.Key).ToList());
        Assert.Equal([LoginA], UnifiedSources.Build([(LoginB, b), (LoginA, a)], LoginA, new UnifiedSettings(true, false, false)).Select(s => s.Key).ToList());
    }

    [Fact]
    public async Task Pages_merge_by_time_and_each_source_pages_on_its_own()
    {
        var (a, b) = await ConnectAsync();
        var source = SourceOf(a, b);
        var query = new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 2 };

        // the shared account cannot be read on this fake server: the other sources still answer
        var first = (await source.QueryAsync(query, null, Ct)).Value!;
        Assert.Equal(["a5", "b4", "a3", "b2"], Subjects(first.Items));
        Assert.Equal(5, first.Total);
        Assert.NotNull(first.Next);
        Assert.All(first.Items, r => Assert.NotNull(UnifiedRows.ParseRow(r.Id)));
        Assert.Equal(LoginB, UnifiedRows.ParseRow(first.Items[1].Id)!.Login);

        var second = (await source.QueryAsync(query, first.Next, Ct)).Value!;
        Assert.Equal(["a1"], Subjects(second.Items));
        Assert.Null(second.Next);
    }

    [Fact]
    public async Task New_mail_in_one_account_comes_back_as_one_added_row_without_a_reset()
    {
        var (a, b) = await ConnectAsync();
        var source = SourceOf(a, b, group: false);
        var query = new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 10 };
        _ = (await source.QueryAsync(query, null, Ct)).Value!;
        Assert.Null((await source.RefreshAsync(Ct)).Value);

        _b.Store.Deliver(new FakeMail { Subject = "b6", ReceivedAt = T0.AddHours(6) }, _inboxB);
        var delta = (await source.RefreshAsync(Ct)).Value!;

        Assert.Empty(delta.Removed);
        var (id, index) = Assert.Single(delta.Added);
        Assert.Equal(0, index);
        Assert.Equal("b6", delta.Emails[id].Subject);
        Assert.Equal(LoginB, UnifiedRows.ParseRow(id)!.Login);
        Assert.Equal(6, delta.Total);
    }

    [Fact]
    public async Task Starring_a_row_of_the_other_account_writes_only_to_that_account()
    {
        var (a, b) = await ConnectAsync();
        var source = SourceOf(a, b, group: false);
        var rows = (await source.QueryAsync(new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 10 }, null, Ct)).Value!.Items;
        var ofB = rows.First(r => string.Equals(UnifiedRows.ParseRow(r.Id)!.Login, LoginB, StringComparison.Ordinal));
        var writer = new UnifiedMailWriter(
            new MailWriter(a, a.Session.MailAccountId),
            row => new MailWriter(string.Equals(row.Login, LoginA, StringComparison.Ordinal) ? a : b, row.SharedAccount ?? (string.Equals(row.Login, LoginA, StringComparison.Ordinal) ? a : b).Session.MailAccountId),
            (_, m, _) => Task.FromResult<string?>(m));
        var setsBeforeA = _a.ApiCalls.Count(c => c.Contains("Email/set", StringComparison.Ordinal));

        var r = await writer.UpdateKeywordsAsync([ofB.Id], new Dictionary<string, bool>(StringComparer.Ordinal) { ["$flagged"] = true }, Ct);

        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal([ofB.Id], r.Value!.Updated);
        Assert.Equal(setsBeforeA, _a.ApiCalls.Count(c => c.Contains("Email/set", StringComparison.Ordinal)));
        Assert.Contains(_b.ApiCalls, c => c.Contains("Email/set", StringComparison.Ordinal));
        var starred = Assert.Single(_b.Store.Emails([UnifiedRows.RealId(ofB.Id)]));
        Assert.True(starred["keywords"]!.AsObject().ContainsKey("$flagged"));
    }

    [Fact]
    public async Task A_folder_picked_in_the_active_account_is_matched_by_role_in_the_other()
    {
        var (a, b) = await ConnectAsync();
        var archiveA = (await UnifiedFetcher.MailboxesAsync(new UnifiedSource(LoginA, a, "a1", false), Ct)).Value!.First(m => string.Equals((string?)m["role"], "archive", StringComparison.Ordinal));
        var inB = (await UnifiedFetcher.MailboxesAsync(new UnifiedSource(LoginB, b, "b1", false), Ct)).Value!;
        var matched = UnifiedRows.MatchMailbox(inB, (string?)archiveA["role"] ?? "", (string?)archiveA["name"] ?? "");
        Assert.Equal((string?)inB.First(m => string.Equals((string?)m["role"], "archive", StringComparison.Ordinal))["id"], matched);
        Assert.NotEqual((string?)archiveA["id"], matched, StringComparer.Ordinal);
    }
}
