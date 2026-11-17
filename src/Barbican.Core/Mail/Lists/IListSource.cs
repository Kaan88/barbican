// Mirrors: M2 plan section 4.4 (IListSource: default is the active account's IMailService and IMailSync exactly as in M1). Contract from stage F of M2; owner unified (wave 2).

using System.Threading.Channels;
using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Lists;

/// <summary>Where a list gets its pages and events from: one account, the unified inbox, a shared account.</summary>
public interface IListSource
{
    /// <summary>One page of the query.</summary>
    Task<Result<ListPage>> QueryAsync(ListQuery query, ListCursor? after, CancellationToken ct);

    /// <summary>The source's own push events, or null when the active account's sync keeps the list current (the M1 path).</summary>
    ChannelReader<MailSyncEvent>? Events { get; }
}
