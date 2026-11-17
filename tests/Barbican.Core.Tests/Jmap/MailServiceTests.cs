// The read side against the fake server: folders and roles (RFC 8621 §2), list pages with anchor paging (RFC 8620 §5.5), threads
// (RFC 8621 §3), bodies (§4.2: bodyProperties, fetch*BodyValues, maxBodyValueBytes, isTruncated), header forms (§4.1.2), identities
// and quota; Rust jmap.rs:406, :524, :648, :666, :831, :1146. Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Jmap;

public sealed class MailServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(Fake F, MailService S)> StartAsync(FakeJmapOptions? options = null, TimeProvider? time = null)
    {
        var f = await Fake.StartAsync(options, time);
        return (f, new MailService(f.Client, f.Time));
    }

    [Fact]
    [Rfc("J-045", "RFC 8621 2")]
    [Rfc("J-021", "RFC 8620 5.1")]
    public async Task Mailboxes_come_with_roles_counts_and_their_state()
    {
        var (f, s) = await StartAsync();
        var tree = (await s.GetMailboxesAsync(Ct)).Value!;
        Assert.Equal(f.Server.Store.State("Mailbox"), tree.State);
        var inbox = Assert.Single(tree.All, m => m.Role == MailboxRole.Inbox);
        Assert.Equal(f.Server.Store.Mailboxes().Single(m => string.Equals((string?)m["role"], "inbox", StringComparison.Ordinal))["unreadEmails"]!.GetValue<int>(), inbox.UnreadEmails);
        Assert.Equal(inbox.Id, Assert.Single(tree.All, m => string.Equals(m.Name, "Projects", StringComparison.Ordinal)).ParentId);
        var roles = (await s.GetRoleIdsAsync(Ct)).Value!;
        Assert.Equal(["archive", "drafts", "inbox", "junk", "sent", "trash"], roles.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    [Fact]
    [Rfc("J-045", "RFC 8621 2")]
    public async Task The_role_map_is_cached_for_five_minutes()
    {
        var clock = Fake.VirtualClock();
        var (f, s) = await StartAsync(time: clock);
        await s.GetRoleIdsAsync(Ct);
        await s.GetRoleIdsAsync(Ct);
        Assert.Single(f.Server.ApiCalls);
        clock.Advance(TimeSpan.FromMinutes(5.1));
        await s.GetRoleIdsAsync(Ct);
        Assert.Equal(2, f.Server.ApiCalls.Count);
    }

    [Fact]
    [Rfc("J-008", "RFC 8620 5.1")]
    public async Task A_tree_cut_at_maxObjectsInGet_is_paged()
    {
        var (f, s) = await StartAsync(new FakeJmapOptions { MaxObjectsInGet = 3 });
        var tree = (await s.GetMailboxesAsync(Ct)).Value!;
        Assert.Equal(7, tree.All.Count);
        Assert.Equal("Mailbox/get", f.Server.ApiCalls[0]);
        Assert.Equal("Mailbox/get", f.Server.ApiCalls[1]);
        Assert.Equal("Mailbox/query", f.Server.ApiCalls[2]);
    }

    [Fact]
    [Rfc("J-050", "RFC 8621 4.4")]
    [Rfc("J-049", "RFC 8621 3")]
    [Rfc("J-014", "RFC 8620 3.7")]
    public async Task A_page_is_newest_first_with_total_states_and_thread_sizes()
    {
        var (f, s) = await StartAsync();
        var inbox = f.Server.Store.MailboxId("inbox")!;
        var page = (await s.QueryAsync(new ListQuery { MailboxId = inbox, PageSize = 50 }, null, Ct)).Value!;
        Assert.Equal("Email/query(limit=50),Email/get,Thread/get -> Email/query,Email/get,Thread/get", f.Trace[^1]);
        Assert.Equal(f.Server.Store.Query(new JsonObject { ["inMailbox"] = inbox }), page.Items.Select(e => e.Id), StringComparer.Ordinal);
        Assert.Equal(page.Items.Count, page.Total);
        Assert.True(page.CanCalculateChanges);
        Assert.Equal(f.Server.Store.State("Email"), page.QueryState);
        Assert.Equal(f.Server.Store.State("Thread"), page.ThreadState);
        Assert.All(page.Items.Where(e => e.Subject!.Contains(FakeSeed.ThreadSubject, StringComparison.Ordinal)), e => Assert.Equal(5, e.ThreadCount));
        Assert.Null(page.Next);
    }

    [Fact]
    [Rfc("J-031", "RFC 8620 5.5")]
    public async Task Load_more_pages_by_anchor_so_new_mail_does_not_shift_it()
    {
        var (f, s) = await StartAsync();
        var q = new ListQuery { MailboxId = f.Server.Store.MailboxId("inbox"), PageSize = 3 };
        var first = (await s.QueryAsync(q, null, Ct)).Value!;
        Assert.NotNull(first.Next);
        f.Server.Store.Deliver(new FakeMail { Subject = "Arrived meanwhile", ReceivedAt = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero) });
        var second = (await s.QueryAsync(q, first.Next, Ct)).Value!;
        Assert.Empty(first.Items.Select(e => e.Id).Intersect(second.Items.Select(e => e.Id), StringComparer.Ordinal));
        Assert.Equal(f.Server.Store.Query(new JsonObject { ["inMailbox"] = q.MailboxId })[4], second.Items[0].Id);
    }

    [Fact]
    [Rfc("J-031", "RFC 8620 5.5")]
    public async Task A_vanished_anchor_falls_back_to_its_position()
    {
        var (f, s) = await StartAsync();
        var q = new ListQuery { MailboxId = f.Server.Store.MailboxId("inbox"), PageSize = 3 };
        var first = (await s.QueryAsync(q, null, Ct)).Value!;
        f.Server.Store.Destroy(first.Next!.AnchorId);
        var second = (await s.QueryAsync(q, first.Next, Ct)).Value!;
        Assert.Equal(3, second.Position);
        Assert.Contains("errors=\"anchorNotFound\"", f.Trace[^2], StringComparison.Ordinal);
    }

    [Fact]
    [Rfc("J-008", "RFC 8620 5.1")]
    public async Task A_page_larger_than_maxObjectsInGet_reads_rows_in_chunks_in_query_order()
    {
        var (f, s) = await StartAsync(new FakeJmapOptions { MaxObjectsInGet = 4 });
        var page = (await s.QueryAsync(new ListQuery { PageSize = 10 }, null, Ct)).Value!;
        Assert.Equal(f.Server.Store.EmailIds().Take(10), page.Items.Select(e => e.Id), StringComparer.Ordinal);
        Assert.Equal("Email/query,Email/get,Thread/get", f.Server.ApiCalls[0]);
        Assert.Equal(f.Server.Store.State("Email"), page.EmailState);
    }

    [Fact]
    [Rfc("J-052", "RFC 8621 4.2")]
    [Rfc("J-054", "RFC 8621 4.1.2")]
    public async Task A_body_asks_for_text_and_html_values_capped_at_2_MB()
    {
        var (f, s) = await StartAsync();
        var large = f.Server.Store.Emails(f.Server.Store.EmailIds()).Single(e => string.Equals((string?)e["subject"], FakeSeed.LargeSubject, StringComparison.Ordinal));
        var body = (await s.GetBodyAsync((string)large["id"]!, new BodyFetchOptions(), Ct)).Value!;
        Assert.True(body.IsTruncated);
        Assert.Equal(2_000_000, body.BodyValues["1"].Value.Length);
        var dark = f.Server.Store.Emails(f.Server.Store.EmailIds()).Single(e => string.Equals((string?)e["subject"], "Dark mode newsletter", StringComparison.Ordinal));
        var html = (await s.GetBodyAsync((string)dark["id"]!, new BodyFetchOptions(), Ct)).Value!;
        Assert.False(html.IsTruncated);
        Assert.Contains("prefers-color-scheme: dark", html.BodyValues[html.HtmlBody[0].PartId!].Value, StringComparison.Ordinal);
        Assert.Contains("dkim=pass", (await s.QueryAsync(new ListQuery(), null, Ct)).Value!.Items[0].AuthenticationResults[0], StringComparison.Ordinal);
        Assert.False((await s.GetBodyAsync("missing", new BodyFetchOptions(), Ct)).IsOk);
    }

    [Fact]
    public async Task Attachments_and_inline_parts_are_read()
    {
        var (f, s) = await StartAsync();
        var id = f.Server.Store.Emails(f.Server.Store.EmailIds()).Single(e => string.Equals((string?)e["subject"], "Report with attachments", StringComparison.Ordinal))["id"]!.GetValue<string>();
        var body = (await s.GetBodyAsync(id, new BodyFetchOptions { MaxBodyValueBytes = 100 }, Ct)).Value!;
        Assert.Equal(["report.pdf", "chart.png"], body.Attachments.Select(a => a.Name), StringComparer.Ordinal);
        Assert.Equal("chart1@example.org", body.Attachments[1].Cid);
        Assert.True(body.HasAttachment);
        Assert.Equal("Carol", body.From[0].Name);
    }

    [Fact]
    [Rfc("J-049", "RFC 8621 3")]
    public async Task A_thread_lists_its_messages_oldest_first()
    {
        var (f, s) = await StartAsync();
        var any = f.Server.Store.Emails(f.Server.Store.EmailIds()).First(e => ((string?)e["subject"])!.Contains(FakeSeed.ThreadSubject, StringComparison.Ordinal));
        var t = (await s.GetThreadAsync((string)any["threadId"]!, Ct)).Value!;
        Assert.Equal(5, t.Emails.Count);
        Assert.Equal(FakeSeed.ThreadSubject, t.Emails[0].Subject);
        Assert.Equal("Thread/get,Email/get", f.Server.ApiCalls[^1]);
    }

    [Fact]
    public async Task Identities_and_quota()
    {
        var (_, s) = await StartAsync();
        var me = Assert.Single((await s.GetIdentitiesAsync(Ct)).Value!);
        Assert.Equal("user@example.org", me.Email);
        var quota = (await s.GetQuotaAsync(Ct)).Value!;
        Assert.Equal((3_500_000L, 1_000_000_000L), (quota.Used, quota.HardLimit));
    }

    [Fact]
    [Rfc("J-055", "RFC 8621 4.1.1")]
    public void Model_reading_skips_invalid_keywords_and_reads_roles()
    {
        var e = ModelReader.Summary(JsonNode.Parse("""{"id":"e1","keywords":{"$seen":true,"bad(":true,"$flagged":false},"subject":null}"""))!;
        Assert.True(e.Keywords.Seen);
        Assert.False(e.Keywords.IsSet("bad("));
        Assert.False(e.Keywords.Flagged);
        Assert.Null(e.Subject);
        Assert.Null(ModelReader.Summary(JsonNode.Parse("""{"subject":"no id"}""")));
        Assert.Equal(MailboxRole.Junk, ModelReader.Mailbox(JsonNode.Parse("""{"id":"m","role":"JUNK"}"""))!.Role);
        Assert.Equal(MailboxRole.None, ModelReader.Mailbox(JsonNode.Parse("""{"id":"m","role":"x-custom"}"""))!.Role);
        Assert.Equal("archive", MailboxRoleConverter.Name(MailboxRole.Archive));
        Assert.Null(MailboxRoleConverter.Name(MailboxRole.None));
        var json = System.Text.Json.JsonSerializer.Serialize(new Mailbox { Id = "m", Role = MailboxRole.Sent, Name = "S" }, JmapJsonContext.Default.Mailbox);
        Assert.Contains("\"role\":\"sent\"", json, StringComparison.Ordinal);
        var kw = System.Text.Json.JsonSerializer.Serialize(new EmailSummary { Id = "e", Keywords = Keywords.Of("$seen") }, JmapJsonContext.Default.EmailSummary);
        Assert.Contains("\"keywords\":{\"$seen\":true}", kw, StringComparison.Ordinal);
    }

    [Fact]
    public void Filters_join_conditions_with_and()
    {
        Assert.Equal("{}", ListFilters.For(new ListQuery()).ToJsonString());
        Assert.Equal("""{"inMailbox":"m1"}""", ListFilters.For(new ListQuery { MailboxId = "m1" }).ToJsonString());
        var all = ListFilters.For(new ListQuery { MailboxId = "m1", SearchText = "  hi ", HasKeyword = "$flagged", NotKeyword = "$seen" });
        Assert.Equal("AND", (string?)all["operator"]);
        Assert.Equal(4, all["conditions"]!.AsArray().Count);
        Assert.Equal("hi", (string?)all["conditions"]![1]!["text"]);
    }

    [Fact]
    public void Endpoints_parse_like_the_rust_normalize_base()
    {
        Assert.Equal("https://mail.example.org/.well-known/jmap", ServerEndpoint.Parse("mail.example.org")!.DiscoveryUri.ToString());
        Assert.Equal("http://localhost:8080/.well-known/jmap", ServerEndpoint.Parse("http://localhost:8080/.well-known/jmap/")!.DiscoveryUri.ToString());
        Assert.Null(ServerEndpoint.Parse(""));
    }

    [Fact]
    [Rfc("J-051", "RFC 8621 4.4.3")]
    public async Task Collapse_threads_is_sent_only_when_asked_and_lists_one_message_per_conversation()
    {
        var (f, s) = await StartAsync();
        var inbox = f.Server.Store.MailboxId("inbox")!;
        var all = (await s.QueryAsync(new ListQuery { MailboxId = inbox, PageSize = 50 }, null, Ct)).Value!;
        var collapsed = (await s.QueryAsync(new ListQuery { MailboxId = inbox, PageSize = 50, CollapseThreads = true }, null, Ct)).Value!;
        Assert.True(collapsed.Items.Count < all.Items.Count);
        Assert.Equal(collapsed.Items.Count, collapsed.Items.Select(e => e.ThreadId).Distinct(StringComparer.Ordinal).Count());
    }
}
