// Mirrors: Bulwark lib/plugin-sandbox/host-api-mail.ts:99-181 (the shared tail of doEmailList / doEmailSearch: a named mailbox must be one the
// login shows, then client.getEmails(mailboxId, account, limit, position, ...) and emailSummary per row). Barbican: IMailService.QueryAsync
// (Email/query + Email/get + Thread/get, RFC 8621 §4.4) with a position cursor; the page is cut at the asked limit.
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Mail;

internal static class PluginMailListing
{
    internal static async Task<PluginCallOutcome> RunAsync(IPluginMailReaders readers, IPluginInvocationContext context, IAccountSession session,
        string? mailboxId, JsonObject? filter, bool? collapseThreads, int limit, int position, CancellationToken ct)
    {
        var service = readers.Reader(session, null);
        if (mailboxId is not null)
        {
            var tree = await service.GetMailboxesAsync(ct).ConfigureAwait(false);
            if (!tree.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
            if (!tree.Value!.All.Any(m => string.Equals(m.Id, mailboxId, StringComparison.Ordinal))) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
            if (filter is not null) filter["inMailbox"] = mailboxId;
        }

        var query = new ListQuery { MailboxId = filter is null ? mailboxId : null, Filter = filter, PageSize = limit, CollapseThreads = collapseThreads };
        var page = await service.QueryAsync(query, position > 0 ? new ListCursor(string.Empty) { Position = position } : null, ct).ConfigureAwait(false);
        if (!page.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        var account = session.Client.Session.MailAccountId;
        var b = new PluginValueBuilder();
        var rows = page.Value!.Items.Take(limit).Select(e => PluginMailProjection.Summary(b, e, account)).ToArray();
        return PluginCallOutcome.Succeeded(b.Build(b.Array(rows), PluginMailProjection.Limits));
    }
}
