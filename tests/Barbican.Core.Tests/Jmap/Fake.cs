// Test wiring over the fake server: a connected client on a virtual or real clock, with the trace captured. Owner jmap.

using Barbican.Core.Jmap.Transport;
using Barbican.FakeJmap;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Jmap;

internal sealed class Fake
{
    private Fake(FakeJmapServer server, JmapClient client, List<string> trace, TimeProvider time)
    {
        Server = server;
        Client = client;
        Trace = trace;
        Time = time;
    }

    public FakeJmapServer Server { get; }

    public JmapClient Client { get; }

    public List<string> Trace { get; }

    public TimeProvider Time { get; }

    public static async Task<Fake> StartAsync(FakeJmapOptions? options = null, TimeProvider? time = null, bool readOnly = false)
    {
        var server = new FakeJmapServer(options);
        var trace = new List<string>();
        var clock = time ?? TimeProvider.System;
        var client = await FakeJmapHarness.ConnectAsync(server, clock, line => { lock (trace) { trace.Add(line); } }, readOnly, TestContext.Current.CancellationToken);
        return new Fake(server, client, trace, clock);
    }

    public static FakeTimeProvider VirtualClock() => new(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
}
