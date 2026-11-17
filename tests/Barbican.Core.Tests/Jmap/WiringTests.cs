// The registration module, the no-plugin hook bus, the account factory, the model converters and the one-time-code sign-in
// (Bulwark lib/jmap/client.ts:2165; Rust jmap.rs:125 `password$code`). Owner jmap.

using System.Text.Json;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Sync;
using Barbican.FakeJmap;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Core.Tests.Jmap;

public sealed class WiringTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void AddBarbicanCore_registers_the_transport_and_a_stand_in_hook_bus()
    {
        var services = new ServiceCollection().AddBarbicanCore();
        Assert.Contains(services, d => d.ServiceType == typeof(IJmapClientFactory));
        Assert.Contains(services, d => d.ServiceType == typeof(IJmapHttp));
        Assert.Contains(services, d => d.ServiceType == typeof(IMailAccountFactory));
        Assert.Same(NullHookBus.Instance, services.Single(d => d.ServiceType == typeof(IHookBus)).ImplementationInstance);
        Assert.Throws<ArgumentNullException>(() => JmapRegistration.AddBarbicanCore(null!));
    }

    [Fact]
    public async Task The_null_bus_listens_to_nothing()
    {
        var bus = NullHookBus.Instance;
        Assert.False(bus.HasHandlers(HookNames.OnSyncStateChange));
        await bus.NotifyAsync(HookNames.OnSyncStateChange, SyncStatePayload.Idle, Ct);
        Assert.True(await bus.InterceptAsync("x", 1, Ct));
        Assert.Equal(3, await bus.TransformAsync("x", 3, Ct));
    }

    [Fact]
    public async Task The_account_factory_makes_a_service_and_a_sync_engine()
    {
        var f = await Fake.StartAsync();
        var factory = new MailAccountFactory(NullHookBus.Instance, TimeProvider.System);
        var service = factory.CreateService(f.Client);
        Assert.Same(f.Client, service.Client);
        using var sync = (MailSync)factory.CreateSync(service);
        sync.Watch(new ListQuery());
        await sync.SyncNowAsync(new HashSet<string>(["Email"], StringComparer.Ordinal), Ct);
        var events = new List<MailSyncEvent>();
        while (sync.Events.TryRead(out var e))
        {
            events.Add(e);
        }

        Assert.Contains(events, e => e is MailboxesChangedEvent);
        Assert.Contains(events, e => e is ListResetEvent);
    }

    [Fact]
    public async Task A_missing_one_time_code_is_asked_for()
    {
        var server = new FakeJmapServer(new FakeJmapOptions { Otp = "123456" });
        var http = new JmapHttp(() => server);
        var uri = new Uri(server.Base + "/.well-known/jmap");
        var without = await SessionFetcher.FetchAsync(http, StaticAccessTokenSource.Basic("user@example.org", "fake-password"), uri, TimeSpan.FromSeconds(5), TimeProvider.System, Ct);
        Assert.IsType<OtpRequiredError>(without.Error);
        var with = await SessionFetcher.FetchAsync(http, StaticAccessTokenSource.Basic("user@example.org", "fake-password", " 123456 "), uri, TimeSpan.FromSeconds(5), TimeProvider.System, Ct);
        Assert.True(with.IsOk);
    }

    [Fact]
    public void Converters_round_trip_roles_and_keywords()
    {
        foreach (var role in Enum.GetValues<MailboxRole>())
        {
            var json = JsonSerializer.Serialize(new Mailbox { Id = "m", Role = role }, JmapJsonContext.Default.Mailbox);
            Assert.Equal(role, JsonSerializer.Deserialize(json, JmapJsonContext.Default.Mailbox)!.Role);
        }

        Assert.Equal(MailboxRole.None, JsonSerializer.Deserialize("""{"id":"m","role":7}""", JmapJsonContext.Default.Mailbox)!.Role);
        Assert.Equal(Keywords.None.Map, JsonSerializer.Deserialize("""{"id":"e","keywords":[1,2]}""", JmapJsonContext.Default.EmailSummary)!.Keywords.Map);
        Assert.Empty(JsonSerializer.Deserialize("""{"id":"e","keywords":null}""", JmapJsonContext.Default.EmailSummary)!.Keywords.Map);
        var kept = JsonSerializer.Deserialize("""{"id":"m","name":"N","x-extra":{"a":1}}""", JmapJsonContext.Default.Mailbox)!;
        Assert.True(kept.Extra!.ContainsKey("x-extra"));
    }
}
