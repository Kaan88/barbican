using System.Net;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.OAuth;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins;

public sealed class PluginOAuthCallbacksTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static PluginPackageIdentity Package(char hash = 'a') => new("probe", "1.0.0", new string(hash, 64));
    private static PluginGrantSnapshot Grant(long generation = 1, bool enabled = true, bool permission = true, char hash = 'a') =>
        new(Package(hash), generation, enabled, permission ? ["http:fetch"] : [], []);
    private static PluginRuntimeBinding Binding(PluginGrantSnapshot grant, PluginFrameKind kind = PluginFrameKind.Background, string profile = "profile-a") =>
        PluginRuntimeBinding.Create(profile, grant, kind, kind == PluginFrameKind.Slot ? "panel" : null);

    [Fact]
    public async Task SuccessIsDeliveredOnlyToOwnerAndConsumedOnce()
    {
        var authority = new Authority(Grant());
        var broker = new PluginOAuthCallbacks(authority, new FakeTimeProvider());
        var owner = new Runtime(Binding(Grant()));
        var other = new Runtime(Binding(Grant(), profile: "profile-b"));
        using var registration = broker.Register(owner);
        using var otherRegistration = broker.Register(other);
        var state = await broker.CreateStateAsync(owner.Binding, Ct);
        Assert.False(string.IsNullOrWhiteSpace(state));
        Assert.True(await broker.DeliverAsync(new(state!, "private-code", null, null), Ct));
        Assert.False(await broker.DeliverAsync(new(state!, "private-code", null, null), Ct));
        var call = Assert.Single(owner.Calls);
        Assert.Equal(HookNames.OnOAuthCallback, call.Name);
        Assert.Equal(PluginValueKind.Array, call.Args.Kind);
        Assert.Equal(1, call.Args.Count);
        var payload = call.Args.Item(0);
        Assert.Equal("private-code", payload.Property("code").StringValue);
        Assert.DoesNotContain("error", payload.PropertyNames, StringComparer.Ordinal);
        Assert.Empty(other.Calls);
    }

    [Fact]
    public async Task ErrorPayloadAndInvalidMutuallyExclusiveFields()
    {
        var broker = new PluginOAuthCallbacks(new Authority(Grant()), new FakeTimeProvider());
        var runtime = new Runtime(Binding(Grant()));
        using var registration = broker.Register(runtime);
        var bad = await broker.CreateStateAsync(runtime.Binding, Ct);
        Assert.False(await broker.DeliverAsync(new(bad!, "code", "denied", null), Ct));
        Assert.False(await broker.DeliverAsync(new("unknown", null, null, null), Ct));
        var state = await broker.CreateStateAsync(runtime.Binding, Ct);
        Assert.True(await broker.DeliverAsync(new(state!, null, "denied", "user refused"), Ct));
        var payload = Assert.Single(runtime.Calls).Args.Item(0);
        Assert.Equal("denied", payload.Property("error").StringValue);
        Assert.Equal("user refused", payload.Property("error_description").StringValue);
        Assert.DoesNotContain("code", payload.PropertyNames, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ExpiredAndRevokedStatesNeverDeliver()
    {
        var clock = new FakeTimeProvider();
        var authority = new Authority(Grant());
        var broker = new PluginOAuthCallbacks(authority, clock);
        var runtime = new Runtime(Binding(Grant()));
        using var registration = broker.Register(runtime);
        var expired = await broker.CreateStateAsync(runtime.Binding, Ct);
        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.False(await broker.DeliverAsync(new(expired!, "secret", null, null), Ct));
        var revoked = await broker.CreateStateAsync(runtime.Binding, Ct);
        authority.Current = Grant(2, permission: false);
        Assert.False(await broker.DeliverAsync(new(revoked!, "secret", null, null), Ct));
        Assert.Empty(runtime.Calls);
    }

    [Fact]
    public async Task OnlyMatchingBackgroundWithLiveGrantCanIssueStateForSlot()
    {
        var authority = new Authority(Grant());
        var broker = new PluginOAuthCallbacks(authority, new FakeTimeProvider());
        var background = new Runtime(Binding(Grant()));
        var slot = Binding(Grant(), PluginFrameKind.Slot);
        Assert.Null(await broker.CreateStateAsync(slot, Ct));
        using var registration = broker.Register(background);
        Assert.NotNull(await broker.CreateStateAsync(slot, Ct));
        Assert.Null(await broker.CreateStateAsync(Binding(Grant(), PluginFrameKind.Slot, "profile-b"), Ct));
        Assert.Null(await broker.CreateStateAsync(Binding(Grant(hash: 'b'), PluginFrameKind.Slot), Ct));
        authority.Current = Grant(enabled: false);
        Assert.Null(await broker.CreateStateAsync(slot, Ct));
    }

    [Fact]
    public async Task ProductionLoopbackRejectsOtherRoutesAndReplaysAndDeliversOnlyToTheOwner()
    {
        var grant = Grant();
        var otherGrant = new PluginGrantSnapshot(new("another-probe", "1.0.0", new string('b', 64)), 1, true, ["http:fetch"], []);
        var authority = new Authority(grant) { Additional = otherGrant };
        var broker = new PluginOAuthCallbacks(authority, TimeProvider.System);
        await using var owner = new Runtime(Binding(grant));
        await using var other = new Runtime(Binding(otherGrant));
        using var registered = broker.Register(owner);
        using var otherRegistered = broker.Register(other);
        await using var endpoint = new PluginOAuthRedirect(broker, TimeProvider.System);
        var redirect = Assert.IsType<Uri>(endpoint.GetOrStart());
        Assert.Equal("127.0.0.1", redirect.Host);
        Assert.Equal("/plugins/oauth/callback", redirect.AbsolutePath);
        Assert.Equal(redirect, endpoint.GetOrStart());
        var state = Assert.IsType<string>(await broker.CreateStateAsync(owner.Binding, Ct));
        Assert.NotNull(await broker.CreateStateAsync(other.Binding, Ct));
        Assert.False(broker.OwnsState(other.Binding, state));

        using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
        var result = new UriBuilder(redirect) { Query = "state=" + state + "&code=synthetic-code" }.Uri;
        var wrongRoute = new UriBuilder(result) { Path = "/callback" }.Uri;
        var duplicateFields = new UriBuilder(result) { Query = result.Query + "&state=" + state }.Uri;
        await ExpectAsync(wrongRoute, HttpStatusCode.BadRequest);
        await ExpectAsync(duplicateFields, HttpStatusCode.BadRequest);
        await ExpectAsync(result, HttpStatusCode.OK);
        await ExpectAsync(result, HttpStatusCode.BadRequest);
        Assert.Equal("synthetic-code", Assert.Single(owner.Calls).Args.Item(0).Property("code").StringValue);
        Assert.Empty(other.Calls);

        async Task ExpectAsync(Uri uri, HttpStatusCode expected)
        {
            using var response = await client.GetAsync(uri, Ct);
            Assert.Equal(expected, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.DoesNotContain("synthetic-code", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ANewFlowKeepsTheSharedRedirectLiveBeyondTheOriginalDeadline()
    {
        var clock = new FakeTimeProvider();
        var grant = Grant();
        var authority = new Authority(grant);
        var broker = new PluginOAuthCallbacks(authority, clock);
        await using var runtime = new Runtime(Binding(grant));
        using var registered = broker.Register(runtime);
        await using var endpoint = new PluginOAuthRedirect(broker, clock);
        var dispatcher = new PluginApiDispatcher(runtime.Binding.Profile,
            PluginOAuthMethods.For(broker, endpoint, authority, launcher: null, confirm: null), authority, new NoSessions(), clock);
        var builder = new PluginValueBuilder();
        var args = builder.Build(builder.Array());
        var redirect = await dispatcher.DispatchAsync(runtime.Binding, new("redirect", "oauth.getRedirectUri", args), Ct);
        Assert.True(redirect.IsSuccess);
        var original = await dispatcher.DispatchAsync(runtime.Binding, new("older-state", "oauth.createState", args), Ct);
        Assert.True(original.IsSuccess);
        clock.Advance(TimeSpan.FromMinutes(14));
        var newer = await dispatcher.DispatchAsync(runtime.Binding, new("new-state", "oauth.createState", args), Ct);
        Assert.True(newer.IsSuccess);
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(redirect.Value!.StringValue, endpoint.Current?.AbsoluteUri);
        Assert.False(await broker.DeliverAsync(new(original.Value!.StringValue, "expired", null, null), Ct));
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
        var callback = new UriBuilder(redirect.Value.StringValue) { Query = "state=" + newer.Value!.StringValue + "&code=synthetic" };
        using var response = await client.GetAsync(callback.Uri, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BrowserLaunchRequiresCodeFlowPkceUserConfirmationAndAnUnspentOwnedState()
    {
        var grant = Grant();
        var authority = new Authority(grant);
        var broker = new PluginOAuthCallbacks(authority, TimeProvider.System);
        await using var owner = new Runtime(Binding(grant));
        using var registered = broker.Register(owner);
        await using var endpoint = new PluginOAuthRedirect(broker, TimeProvider.System);
        var launcher = new Browser();
        var prompts = new List<Uri>();
        var allow = false;
        var methods = PluginOAuthMethods.For(broker, endpoint, authority, launcher, (_, uri, _) =>
        {
            prompts.Add(uri);
            return Task.FromResult(allow);
        });
        var dispatcher = new PluginApiDispatcher(owner.Binding.Profile, methods, authority, new NoSessions());
        var redirect = await CallAsync("oauth.getRedirectUri");
        var issued = await CallAsync("oauth.createState");
        Assert.True(redirect.IsSuccess);
        Assert.True(issued.IsSuccess);
        var pairing = $"state={issued.Value!.StringValue}&redirect_uri={Uri.EscapeDataString(redirect.Value!.StringValue)}";
        const string provider = "https://identity.example.org/authorize?";
        const string parameters = "&response_type=code&client_id=fixture&code_challenge_method=S256&code_challenge=";
        var challenge = Pkce.Challenge(Pkce.NewVerifier());
        Assert.False((await CallAsync("ui.openExternalUrl", provider + pairing)).IsSuccess);
        Assert.False((await CallAsync("ui.openExternalUrl", "https://127.0.0.1/authorize?" + pairing + parameters + challenge)).IsSuccess);
        Assert.Empty(prompts);
        Assert.False((await CallAsync("ui.openExternalUrl", provider + pairing + parameters + challenge)).IsSuccess);
        Assert.Empty(launcher.Opened);
        allow = true;
        Assert.False((await CallAsync("ui.openExternalUrl", provider + pairing + parameters + challenge)).IsSuccess);
        Assert.Single(prompts);

        issued = await CallAsync("oauth.createState");
        Assert.True(issued.IsSuccess);
        pairing = $"state={issued.Value!.StringValue}&redirect_uri={Uri.EscapeDataString(redirect.Value!.StringValue)}";
        Assert.True((await CallAsync("ui.openExternalUrl", provider + pairing + parameters + challenge)).IsSuccess);
        Assert.Equal("identity.example.org", Assert.Single(launcher.Opened).Host);

        Task<PluginCallOutcome> CallAsync(string method, string? value = null)
        {
            var builder = new PluginValueBuilder();
            var args = builder.Build(value is null ? builder.Array() : builder.Array(builder.String(value)));
            return dispatcher.DispatchAsync(owner.Binding, new PluginApiRequest("probe", method, args), Ct);
        }
    }

    [Theory]
    [InlineData("POST /plugins/oauth/callback?state=ignored&code=probe HTTP/1.1")]
    [InlineData("GET /plugins/oauth/callback?state=ignored&%73tate=duplicate HTTP/1.1")]
    [InlineData("GET /plugins/oauth/callback?state=ignored#fragment HTTP/1.1")]
    [InlineData("GET http://127.0.0.1/plugins/oauth/callback?state=ignored HTTP/1.1")]
    [InlineData("GET /plugins/oauth/callback?code=probe HTTP/1.1")]
    public void CallbackParserRejectsWrongMethodsAndMalformedTargets(string request) =>
        Assert.Null(LoopbackListener.ReadPluginCallback(request));

    private sealed class Authority(PluginGrantSnapshot initial) : IPluginDeliveryAuthority
    {
        public PluginGrantSnapshot Current { get; set; } = initial;
        public PluginGrantSnapshot? Additional { get; init; }
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        private PluginGrantSnapshot? Get(string id) =>
            string.Equals(id, Current.Package.Id, StringComparison.Ordinal) ? Current :
                string.Equals(id, Additional?.Package.Id, StringComparison.Ordinal) ? Additional : null;
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) =>
            Task.FromResult(Result<PluginGrantSnapshot?>.Ok(Get(pluginId)));
        public Task<bool> TryPostAsync(PluginRuntimeBinding binding, Func<PluginGrantSnapshot, bool> write, CancellationToken ct)
        {
            var grant = Get(binding.Package.Id);
            return Task.FromResult(!ct.IsCancellationRequested && binding.IsAlive && grant is not null &&
                grant.Authorizes(binding.Package, binding.GrantGeneration, "http:fetch", null) && write(grant));
        }
    }

    private sealed class Browser : IAuthorizationLauncher
    {
        public List<Uri> Opened { get; } = [];
        public Task<Result<Unit>> OpenAsync(Uri authorizeUri, CancellationToken ct)
        {
            Opened.Add(authorizeUri);
            return Task.FromResult(Result<Unit>.Ok(default));
        }
    }

    private sealed class NoSessions : IPluginSessionSource
    {
        public Result<PluginSessionCapture> CaptureActive() => throw new NotSupportedException();
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => throw new NotSupportedException();
        public bool IsCurrent(PluginSessionCapture capture) => false;
    }
    private sealed class Runtime(PluginRuntimeBinding binding) : IPluginRuntimeInstance
    {
        public PluginRuntimeBinding Binding { get; } = binding;
        public PluginActivation Activation { get; } = new([HookNames.OnOAuthCallback], [], []);
        public List<(string Name, PluginValue Args)> Calls { get; } = [];
        public Task<Result<PluginValue>> InvokeHookAsync(string hookName, PluginValue arguments, TimeSpan budget, CancellationToken ct)
        {
            Calls.Add((hookName, arguments));
            var b = new PluginValueBuilder();
            return Task.FromResult(Result<PluginValue>.Ok(b.Build(b.Null())));
        }
        public Task<Result<PluginValue>> InvokeCallbackAsync(string callbackId, PluginValue arguments, TimeSpan budget, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<bool>> ShouldShowAsync(string slot, PluginValue context, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<Unit>> SendAsync(PluginValue hostMessage, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => Binding.DisposeAsync();
    }
}
