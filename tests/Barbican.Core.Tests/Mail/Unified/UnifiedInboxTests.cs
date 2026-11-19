// New tests (M2 plan 6.5 exit tests) on FakeJmap with two logins and a shared account: the unified inbox merges by time with per-source
// paging, a source that fails does not empty the list, a new mail comes back as one added row (a delta, no reset), and an action on a
// unified row writes only to that row's account (each server's request log). Rust unified.rs:361 fetch, :474 shared_apply, mail.rs
// on_unified_push. Owner unified.

using Barbican.Core.Jmap;
using Barbican.Core.Common;
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
        Assert.Equal(["a5", "b4"], Subjects(first.Items));
        Assert.Equal(5, first.Total);
        Assert.NotNull(first.Next);
        Assert.All(first.Items, r => Assert.NotNull(UnifiedRows.ParseRow(r.Id)));
        Assert.Equal(LoginB, UnifiedRows.ParseRow(first.Items[1].Id)!.Login);

        var second = (await source.QueryAsync(query, first.Next, Ct)).Value!;
        Assert.Equal(["a3", "b2"], Subjects(second.Items));
        Assert.NotNull(second.Next);
        var third = (await source.QueryAsync(query, second.Next, Ct)).Value!;
        Assert.Equal(["a1"], Subjects(third.Items));
        Assert.Null(third.Next);
    }

    [Fact]
    public async Task Server_page_caps_do_not_hide_newer_rows_behind_other_accounts()
    {
        using var capped = new FakeJmapServer(new FakeJmapOptions { Origin = "https://capped.example.org", AccountId = "c", QueryLimitCap = 1 }, seed: false);
        var inbox = capped.Store.AddMailbox("Inbox", "inbox");
        capped.Store.Deliver(new FakeMail { Subject = "c9", ReceivedAt = T0.AddHours(9) }, inbox);
        capped.Store.Deliver(new FakeMail { Subject = "c8", ReceivedAt = T0.AddHours(8) }, inbox);
        var (a, _) = await ConnectAsync();
        var c = await FakeJmapHarness.ConnectAsync(capped, TimeProvider.System, ct: Ct);
        var source = SourceOf(a, c, group: false);
        var query = new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 2 };
        var page = (await source.QueryAsync(query, null, Ct)).Value!;
        Assert.Equal(["c9", "c8"], Subjects(page.Items));
        Assert.Equal(["a5", "a3"], Subjects((await source.QueryAsync(query, page.Next, Ct)).Value!.Items));
    }

    [Fact]
    public async Task Starred_priority_is_sorted_before_paginating_across_sources()
    {
        _a.Store.Deliver(new FakeMail { Subject = "starA", ReceivedAt = T0, Keywords = ["$flagged"] }, _inboxA);
        _b.Store.Deliver(new FakeMail { Subject = "starB", ReceivedAt = T0.AddMinutes(1), Keywords = ["$flagged"] }, _inboxB);
        var (a, b) = await ConnectAsync();
        var source = SourceOf(a, b, group: false);
        var query = new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 1,
            Sort = [new("hasKeyword", false, "$flagged"), new("receivedAt", false)] };
        var first = (await source.QueryAsync(query, null, Ct)).Value!;
        var second = (await source.QueryAsync(query, first.Next, Ct)).Value!;
        var third = (await source.QueryAsync(query, second.Next, Ct)).Value!;
        Assert.Equal(["starB"], Subjects(first.Items));
        Assert.Equal(["starA"], Subjects(second.Items));
        Assert.Equal(["a5"], Subjects(third.Items));
    }

    [Fact]
    public async Task One_unsupported_account_forces_common_value_sort_and_warning()
    {
        using var refusing = new FakeJmapServer(new FakeJmapOptions { Origin = "https://refusing.example.org", AccountId = "r", RefuseKeywordSort = true }, seed: false);
        var inbox = refusing.Store.AddMailbox("Inbox", "inbox");
        refusing.Store.Deliver(new FakeMail { Subject = "refusing-new", ReceivedAt = T0.AddHours(6) }, inbox);
        _a.Store.Deliver(new FakeMail { Subject = "star-old", ReceivedAt = T0, Keywords = ["$flagged"] }, _inboxA);
        var (a, _) = await ConnectAsync();
        var r = await FakeJmapHarness.ConnectAsync(refusing, TimeProvider.System, ct: Ct);
        var source = SourceOf(a, r, group: false);
        var query = new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 2,
            Sort = [new("hasKeyword", false, "$flagged"), new("receivedAt", false)] };
        var page = (await source.QueryAsync(query, null, Ct)).Value!;
        Assert.NotNull(page.Warning);
        Assert.Equal(["refusing-new", "a5"], Subjects(page.Items));
    }

    [Fact]
    public void Refresh_reordering_emits_remove_and_reinsert_operations()
    {
        var before = new[] { new EmailSummary { Id = "a" }, new EmailSummary { Id = "b" } };
        var after = new[] { before[1], before[0] };
        var delta = UnifiedListSource.Diff(before, after, new Dictionary<string, int>(StringComparer.Ordinal), 2)!;
        Assert.Equal(["a", "b"], delta.Removed);
        Assert.Equal(new[] { ("b", 0), ("a", 1) }, delta.Added);
        Assert.Equal(2, delta.Emails.Count);
    }

    [Fact]
    public async Task Paging_after_refresh_uses_updated_source_positions()
    {
        var (a, b) = await ConnectAsync();
        var source = SourceOf(a, b, group: false);
        var query = new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 2 };
        var first = (await source.QueryAsync(query, null, Ct)).Value!;
        _a.Store.Deliver(new FakeMail { Subject = "a6", ReceivedAt = T0.AddHours(6) }, _inboxA);
        Assert.NotNull((await source.RefreshAsync(Ct)).Value);
        var next = (await source.QueryAsync(query, first.Next, Ct)).Value!;
        Assert.Equal(["a3", "b2"], Subjects(next.Items));
    }

    [Fact]
    public async Task Refresh_keeps_failed_account_total_when_another_account_changes()
    {
        var (a, b) = await ConnectAsync();
        var both = UnifiedSources.Build([(LoginA, a), (LoginB, b)], LoginA, new UnifiedSettings(true, true, false));
        IReadOnlyList<UnifiedSource> current = both;
        var source = new UnifiedListSource(() => current);
        _ = await source.QueryAsync(new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 10 }, null, Ct);
        current = [both[0], both[1] with { AccountId = "missing" }];
        _a.Store.Deliver(new FakeMail { Subject = "a6", ReceivedAt = T0.AddHours(6) }, _inboxA);
        var delta = (await source.RefreshAsync(Ct)).Value!;
        Assert.Equal(6, delta.Total);
        Assert.Empty(delta.Removed);
        Assert.Single(delta.Added);
    }

    [Fact]
    public async Task A_refresh_reports_a_new_keyword_sort_refusal()
    {
        var (a, b) = await ConnectAsync();
        var source = SourceOf(a, b, group: false);
        var query = new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 2,
            Sort = [new("hasKeyword", false, "$flagged"), new("receivedAt", false)] };
        Assert.Null((await source.QueryAsync(query, null, Ct)).Value!.Warning);
        _a.Inject(FakeFault.UnsupportedKeywordSort);
        var delta = (await source.RefreshAsync(Ct)).Value!;
        Assert.NotNull(delta.Warning);
    }

    [Fact]
    public async Task A_new_first_page_fences_an_older_delayed_first_page()
    {
        var (a, b) = await ConnectAsync();
        var delayed = new DelayedClient(a);
        var source = SourceOf(delayed, b, group: false);
        var gate = delayed.Arm();
        var old = source.QueryAsync(new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 2 }, null, Ct);
        await gate.Entered.Task.WaitAsync(Ct);
        var newerQuery = new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 2, SearchText = "subject:b4" };
        var newer = source.QueryAsync(newerQuery, null, Ct);
        gate.Release.SetResult();
        Assert.False((await old).IsOk);
        Assert.Equal(["b4"], Subjects((await newer).Value!.Items));
        Assert.Equal(newerQuery, source.Query);
    }

    [Fact]
    public async Task Load_more_waits_for_refresh_and_uses_its_updated_window()
    {
        var (a, b) = await ConnectAsync();
        var delayed = new DelayedClient(a);
        var source = SourceOf(delayed, b, group: false);
        var query = new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 2 };
        var first = (await source.QueryAsync(query, null, Ct)).Value!;
        _a.Store.Deliver(new FakeMail { Subject = "a6", ReceivedAt = T0.AddHours(6) }, _inboxA);
        var gate = delayed.Arm();
        var refresh = source.RefreshAsync(Ct);
        await gate.Entered.Task.WaitAsync(Ct);
        var next = source.QueryAsync(query, first.Next, Ct);
        Assert.False(next.IsCompleted);
        gate.Release.SetResult();
        Assert.NotNull((await refresh).Value);
        Assert.Equal(["a3", "b2"], Subjects((await next).Value!.Items));
    }

    [Fact]
    public async Task Disposing_with_an_active_read_and_waiter_defers_gate_disposal()
    {
        var (a, b) = await ConnectAsync();
        var delayed = new DelayedClient(a);
        var source = SourceOf(delayed, b, group: false);
        var gate = delayed.Arm();
        var query = new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 2 };
        var active = source.QueryAsync(query, null, Ct);
        await gate.Entered.Task.WaitAsync(Ct);
        var waiting = source.QueryAsync(query, null, Ct);
        source.Dispose();
        gate.Release.SetResult();
        Assert.False((await active).IsOk);
        Assert.False((await waiting).IsOk);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => source.QueryAsync(query, null, Ct));
    }

    [Fact]
    public async Task A_query_switch_discards_a_delayed_refresh()
    {
        var (a, b) = await ConnectAsync();
        var delayed = new DelayedClient(a);
        var source = SourceOf(delayed, b, group: false);
        _ = await source.QueryAsync(new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 2 }, null, Ct);
        _a.Store.Deliver(new FakeMail { Subject = "a6", ReceivedAt = T0.AddHours(6) }, _inboxA);
        var gate = delayed.Arm();
        var refresh = source.RefreshAsync(Ct);
        await gate.Entered.Task.WaitAsync(Ct);
        var newerQuery = new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 2, SearchText = "subject:b4" };
        var newer = source.QueryAsync(newerQuery, null, Ct);
        gate.Release.SetResult();
        Assert.Null((await refresh).Value);
        Assert.Equal(["b4"], Subjects((await newer).Value!.Items));
        Assert.Equal(newerQuery, source.Query);
    }

    private sealed class DelayGate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class DelayedClient(IJmapClient inner) : IJmapClient
    {
        private DelayGate? _next;
        public JmapSession Session => inner.Session;
        public bool IsReadOnly => inner.IsReadOnly;
        public DelayGate Arm() => _next = new DelayGate();
        public T? AccountCapability<T>(string accountId, string capability) where T : class => inner.AccountCapability<T>(accountId, capability);
        public Task<Result<Stream>> DownloadAsync(string accountId, string blobId, string name, string type, CancellationToken ct) => inner.DownloadAsync(accountId, blobId, name, type, ct);
        public async Task<Result<JmapResponse>> SendAsync(JmapRequest request, CancellationToken ct)
        {
            if (Interlocked.Exchange(ref _next, null) is { } gate)
            {
                gate.Entered.SetResult();
                await gate.Release.Task.WaitAsync(ct);
            }

            return await inner.SendAsync(request, ct);
        }
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
    public async Task A_source_that_fails_on_a_refresh_keeps_its_rows_and_a_source_no_longer_read_loses_them()
    {
        var (a, b) = await ConnectAsync();
        var both = UnifiedSources.Build([(LoginA, a), (LoginB, b)], LoginA, new UnifiedSettings(true, CrossAccount: true, IncludeGroup: false));
        IReadOnlyList<UnifiedSource> now = both;
        var source = new UnifiedListSource(() => now);
        _ = (await source.QueryAsync(new ListQuery { MailboxId = UnifiedRows.FolderId, PageSize = 10 }, null, Ct)).Value!;

        // B is still read but its server refuses this time (an account id it does not know): its rows stay, nothing changes
        now = [both[0], both[1] with { AccountId = "missing" }];
        Assert.Null((await source.RefreshAsync(Ct)).Value);

        // B is no longer read (signed out): its two rows go, A's stay
        now = [both[0]];
        var delta = (await source.RefreshAsync(Ct)).Value!;
        Assert.Equal(2, delta.Removed.Count);
        Assert.All(delta.Removed, id => Assert.Equal(LoginB, UnifiedRows.ParseRow(id)!.Login));
        Assert.Empty(delta.Added);
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

    [Fact]
    public async Task Unified_search_reads_the_same_grammar_as_the_folder_search()
    {
        // R3-09: has:attachment and subject: are operators, not words sent to the server as typed
        _a.Store.Deliver(new FakeMail { Subject = "withfile", ReceivedAt = T0.AddHours(6), Attachments = [new FakeAttachment("r.pdf", "application/pdf", [1, 2, 3])] }, _inboxA);
        var (a, b) = await ConnectAsync();
        var source = SourceOf(a, b);
        var withFile = (await source.QueryAsync(new ListQuery { MailboxId = UnifiedRows.FolderId, SearchText = "has:attachment", PageSize = 10 }, null, Ct)).Value!;
        Assert.Equal(["withfile"], Subjects(withFile.Items));
        var bySubject = (await source.QueryAsync(new ListQuery { MailboxId = UnifiedRows.FolderId, SearchText = "subject:b4", PageSize = 10 }, null, Ct)).Value!;
        Assert.Equal(["b4"], Subjects(bySubject.Items));
        var plain = (await source.QueryAsync(new ListQuery { MailboxId = UnifiedRows.FolderId, SearchText = "a3", PageSize = 10 }, null, Ct)).Value!;
        Assert.Equal(["a3"], Subjects(plain.Items));
    }
}
