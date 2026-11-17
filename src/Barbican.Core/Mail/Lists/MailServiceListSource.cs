// Mirrors: M2 plan section 4.4 (the default IListSource): a thin adapter over one account's IMailService, byte for byte the M1 query path. Stage F of M2.

using System.Threading.Channels;
using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Lists;

/// <summary>Answers queries with one account's mail service.</summary>
public sealed class MailServiceListSource(IMailService mail) : IListSource
{
    /// <inheritdoc />
    public ChannelReader<MailSyncEvent>? Events => null;

    /// <inheritdoc />
    public Task<Result<ListPage>> QueryAsync(ListQuery query, ListCursor? after, CancellationToken ct) => mail.QueryAsync(query, after, ct);
}
