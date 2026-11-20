// Synthetic dispatcher-level tests for the M6b http.fetch adapter; no network access.
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Net;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Storage;

namespace Barbican.Core.Tests.Plugins;

public sealed class PluginHttpFetchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly PluginPackageIdentity Package = new("probe", "1.0.0", new string('a', 64));
    private static PluginGrantSnapshot Grant(string[] origins, long generation = 1, string[]? permissions = null) =>
        new(Package, generation, true, permissions ?? ["http:fetch"], [], origins);
    private static PluginApiRequest Request(string url, string options = "{}")
    {
        using var doc = JsonDocument.Parse($"[{JsonSerializer.Serialize(url)},{options}]");
        return new("request", "http.fetch", PluginValue.FromJson(doc.RootElement));
    }

    [Theory]
    [InlineData("https://api.example.org", "https://api.example.org/path", true)]
    [InlineData("https://api.example.org", "https://other.example.org/path", false)]
    [InlineData("https://api.example.org", "https://api.example.org:8443/path", false)]
    [InlineData("https://api.example.org:8443", "https://api.example.org:8443/path", true)]
    [InlineData("https://*.example.org", "https://child.example.org/path", true)]
    [InlineData("https://*.example.org", "https://a.child.example.org/path", false)]
    [InlineData("https://*.example.org", "https://example.org/path", false)]
    [InlineData("https://*.example.org", "https://notexample.org/path", false)]
    [InlineData("https://*.example.org", "https://child.example.org:8443/path", false)]
    public async Task OnlyApprovedOriginAndPortReachTransport(string origin, string url, bool allowed)
    {
        var rig = new Rig(Grant([origin]));
        var result = await rig.CallAsync(url);
        Assert.Equal(allowed, result.IsSuccess);
        Assert.Equal(allowed ? 1 : 0, rig.Sent.Count);
    }

    [Fact]
    public async Task MissingFetchPermissionAndEmptyOriginGrantNeverSend()
    {
        var noPermission = new Rig(Grant([], permissions: []));
        Assert.False((await noPermission.CallAsync("https://api.example.org/path")).IsSuccess);
        Assert.Empty(noPermission.Sent);
        var noOrigins = new Rig(Grant([]));
        Assert.False((await noOrigins.CallAsync("https://api.example.org/path")).IsSuccess);
        Assert.Empty(noOrigins.Sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public async Task ThirdPartyPolicyDefaultsToBlockedAndExplicitTrueBlocks(bool? blocked)
    {
        var rig = new Rig(Grant(["https://api.example.org"]), blocked);
        Assert.False((await rig.CallAsync("https://api.example.org/path")).IsSuccess);
        Assert.Empty(rig.Sent);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(303)]
    public async Task PostRedirectRewritesToGetAndDropsBody(int status)
    {
        var rig = new Rig(Grant(["https://api.example.org"]));
        rig.Replies.Enqueue(Redirect(status, "https://api.example.org/next"));
        var result = await rig.CallAsync("https://api.example.org/start", "{\"method\":\"POST\",\"body\":\"payload\"}");
        Assert.True(result.IsSuccess);
        Assert.Equal(2, rig.Sent.Count);
        Assert.Equal(HttpMethod.Post, rig.Sent[0].Method);
        Assert.NotNull(rig.Sent[0].Body);
        Assert.Equal(HttpMethod.Get, rig.Sent[1].Method);
        Assert.True(rig.Sent[1].Body is null || rig.Sent[1].Body!.Length == 0);
    }

    [Fact]
    public async Task SameOrigin307PreservesMethodAndBody()
    {
        var rig = new Rig(Grant(["https://api.example.org"]));
        rig.Replies.Enqueue(Redirect(307, "https://api.example.org/next"));
        Assert.True((await rig.CallAsync("https://api.example.org/start", "{\"method\":\"POST\",\"body\":\"payload\"}")).IsSuccess);
        Assert.Equal(2, rig.Sent.Count);
        Assert.Equal(HttpMethod.Post, rig.Sent[1].Method);
        Assert.NotNull(rig.Sent[0].Body);
        Assert.Equal(rig.Sent[0].Body, rig.Sent[1].Body);
    }

    [Fact]
    public async Task CrossOrigin307NeverReplaysBodyEvenWhenBothOriginsApproved()
    {
        var rig = new Rig(Grant(["https://api.example.org", "https://other.example.org"]));
        rig.Replies.Enqueue(Redirect(307, "https://other.example.org/next"));
        Assert.False((await rig.CallAsync("https://api.example.org/start", "{\"method\":\"POST\",\"body\":\"payload\"}")).IsSuccess);
        Assert.Single(rig.Sent);
    }

    [Fact]
    public async Task CrossOriginRedirectStripsCredentials()
    {
        var rig = new Rig(Grant(["https://api.example.org", "https://other.example.org"]));
        rig.Replies.Enqueue(Redirect(303, "https://other.example.org/next"));
        var options = "{\"headers\":{\"Authorization\":\"Bearer synthetic\",\"Cookie\":\"synthetic=1\",\"X-Test\":\"ok\"}}";
        Assert.True((await rig.CallAsync("https://api.example.org/start", options)).IsSuccess);
        Assert.Equal(2, rig.Sent.Count);
        Assert.DoesNotContain(rig.Sent[1].Headers.Keys, h => string.Equals(h, "Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(rig.Sent[1].Headers.Keys, h => string.Equals(h, "Cookie", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RevokedGrantBetweenRedirectsNeverSendsSecondRequest()
    {
        var rig = new Rig(Grant(["https://api.example.org"]));
        rig.Replies.Enqueue(Redirect(303, "https://api.example.org/next"));
        rig.AfterSend = () => rig.Authority.Current = Grant(["https://api.example.org"], generation: 2);
        Assert.False((await rig.CallAsync("https://api.example.org/start")).IsSuccess);
        Assert.Single(rig.Sent);
    }

    [Fact]
    public async Task ProductionTransportRefusesMixedPublicAndPrivateResolution()
    {
        var queries = 0;
        using var transport = new GuardedHttp(false, (_, _) =>
        {
            queries++;
            return Task.FromResult<IPAddress[]>([IPAddress.Parse("93.184.215.14"), IPAddress.Loopback]);
        }, TimeProvider.System, GuardedHttp.PluginBodyLimit, TimeSpan.FromSeconds(2));
        var reply = await transport.SendPluginHopAsync(new(new Uri("https://api.example.org"), HttpMethod.Get,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null), Ct);
        Assert.Null(reply);
        Assert.Equal(1, queries);
    }

    private static PluginFetchReply Redirect(int status, string location) => new(status, "redirect", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), [], new Uri(location));
    private sealed class Rig
    {
        private readonly PluginApiDispatcher _dispatcher;
        private readonly PluginGrantSnapshot _grant;
        public Rig(PluginGrantSnapshot grant, bool? blocked = false)
        {
            _grant = grant; Authority = new(grant);
            var values = new JsonObject();
            if (blocked.HasValue) values[PluginNetworkPolicy.Preference] = blocked.Value;
            _dispatcher = new("auto-fetch", [new PluginHttpFetch(Authority, SendAsync, new Prefs(values))], Authority, new NoSessions());
        }
        public Authority Authority { get; }
        public List<PluginFetchRequest> Sent { get; } = [];
        public Queue<PluginFetchReply> Replies { get; } = [];
        public Action? AfterSend { get; set; }
        private Task<PluginFetchReply?> SendAsync(PluginFetchRequest request, CancellationToken ct)
        {
            Sent.Add(request); AfterSend?.Invoke();
            return Task.FromResult<PluginFetchReply?>(Replies.Count > 0 ? Replies.Dequeue() : new(200, "OK", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []));
        }
        public async Task<PluginCallOutcome> CallAsync(string url, string options = "{}")
        {
            await using var binding = PluginRuntimeBinding.Create("auto-fetch", _grant, PluginFrameKind.Background);
            return await _dispatcher.DispatchAsync(binding, Request(url, options), Ct);
        }
    }
    private sealed class Prefs(JsonObject values) : IPrefsStore
    {
        public PrefsSnapshot Current { get; } = PrefsSnapshot.FromJson(values);
        public event EventHandler? Changed { add { } remove { } }
    }
    private sealed class Authority(PluginGrantSnapshot initial) : IPluginDeliveryAuthority
    {
        public PluginGrantSnapshot Current { get; set; } = initial;
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) => Task.FromResult(Result<PluginGrantSnapshot?>.Ok(Current));
        public Task<bool> TryPostAsync(PluginRuntimeBinding binding, Func<PluginGrantSnapshot, bool> write, CancellationToken ct) =>
            Task.FromResult(!ct.IsCancellationRequested && binding.IsAlive && Current.Authorizes(binding.Package, binding.GrantGeneration, "http:fetch", null) && write(Current));
    }
    private sealed class NoSessions : IPluginSessionSource
    {
        public Result<PluginSessionCapture> CaptureActive() => throw new NotSupportedException();
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => throw new NotSupportedException();
        public bool IsCurrent(PluginSessionCapture capture) => false;
    }
}
