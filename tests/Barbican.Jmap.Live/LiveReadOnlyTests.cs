// Read-only checks against the real server (docs/milestones/M1-plan.md §5 jmap "Jmap.Live"): the session, Mailbox/get, Email/query +
// get on Sent, changes since a fresh state. Skipped unless BARBICAN_LIVE=1; the client refuses every write. Owner jmap.

using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Sync;

namespace Barbican.Jmap.Live;

public sealed class LiveReadOnlyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_session_names_a_mail_account_and_the_core_limits()
    {
        var client = await LiveAccount.ConnectAsync(Ct);
        Assert.False(string.IsNullOrEmpty(client.Session.MailAccountId));
        Assert.True(client.Session.Limits.MaxCallsInRequest > 0);
        Assert.True(client.Session.Limits.MaxObjectsInGet > 0);
    }

    [Fact]
    public async Task The_folder_tree_has_an_inbox()
    {
        var service = new MailService(await LiveAccount.ConnectAsync(Ct), TimeProvider.System);
        var tree = await service.GetMailboxesAsync(Ct);
        Assert.True(tree.IsOk, tree.Error?.Message);
        var roles = await service.GetRoleIdsAsync(Ct);
        Assert.True(roles.Value!.ContainsKey("inbox"));
    }

    [Fact]
    public async Task Sent_reads_a_page_and_a_delta_from_its_fresh_state_is_empty()
    {
        var client = await LiveAccount.ConnectAsync(Ct);
        var service = new MailService(client, TimeProvider.System);
        var roles = (await service.GetRoleIdsAsync(Ct)).Value!;
        if (!roles.TryGetValue("sent", out var sent))
        {
            Assert.Skip("the account has no Sent folder");
        }

        var query = new ListQuery { MailboxId = sent, PageSize = 10 };
        var page = await service.QueryAsync(query, null, Ct);
        Assert.True(page.IsOk, page.Error?.Message);
        Assert.InRange(page.Value!.Items.Count, 0, 10);
        Assert.All(page.Value.Items, e => Assert.True(e.MailboxIds.ContainsKey(sent)));
        var states = ListStates.Of(page.Value);
        Assert.NotNull(states);
        var delta = await ListDeltaFetcher.FetchAsync(client, ListFilters.For(query), states, Ct);
        Assert.True(delta.IsOk, delta.Error?.Message);
        if (delta.Value is { } d)
        {
            // mail may arrive between the two requests; nothing is removed from Sent by reading it
            Assert.Empty(d.Removed);
        }
    }
}
