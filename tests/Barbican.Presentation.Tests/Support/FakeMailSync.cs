// Test double for IMailSync: a channel the test writes events to. Owner mailcore.

using System.Threading.Channels;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Tests.Support;

internal sealed class FakeMailSync : IMailSync
{
    private readonly Channel<MailSyncEvent> _channel = Channel.CreateUnbounded<MailSyncEvent>();

    public List<ListQuery> Watched { get; } = [];

    public ChannelReader<MailSyncEvent> Events => _channel.Reader;

    /// <summary>The pages handed to <see cref="Watch(ListQuery, ListPage)"/>: their states are what lets a push become a delta.</summary>
    public List<ListPage> Loaded { get; } = [];

    public void Watch(ListQuery query) => Watched.Add(query);

    public void Watch(ListQuery query, ListPage loaded)
    {
        Watched.Add(query);
        Loaded.Add(loaded);
    }

    public async Task RunAsync(CancellationToken ct) => await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);

    public void Send(MailSyncEvent e) => _channel.Writer.TryWrite(e);
}
