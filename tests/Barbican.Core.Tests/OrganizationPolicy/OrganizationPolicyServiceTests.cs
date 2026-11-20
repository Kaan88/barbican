using System.Net;
using System.Text;
using Barbican.Core.Accounts;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail;
using Barbican.Core.OrganizationPolicy;
using Barbican.Core.Tests.Auth;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.OrganizationPolicy;

public sealed class OrganizationPolicyServiceTests
{
    private const string Key = "sample";
    private const string Access = "synthetic-access-token-123";
    private static readonly Uri PolicyOrigin = new("https://policy.example.org/");
    private const string Policy = """{"organisation":{"devices":{"noPrint":false}},"scopes":[{"id":"example","match":{"domains":["example.org"]},"policy":{"organisation":{"devices":{"noPrint":true}}}}]}""";

    [Fact]
    public async Task OAuth_route_supplies_bound_bearer_and_resolves_scope()
    {
        using var rig = await Rig.CreateAsync();
        var result = await rig.Service.ReadAsync(Key, CancellationToken.None);
        Assert.Equal(OrganizationPolicyTransportStatus.Authoritative, result.Status);
        Assert.True(result.Snapshot!.Effective.NoPrint);
        Assert.Equal(["example"], result.Snapshot.Effective.MatchedScopes, StringComparer.Ordinal);
        Assert.Equal("https://policy.example.org/api/admin/policy", rig.Handler.Target);
        Assert.Equal(rig.Server.AbsoluteUri, rig.Handler.Server);
        Assert.Equal("Bearer", rig.Handler.Scheme);
        Assert.Equal(Access, rig.Handler.Token);
        Assert.True(result.Snapshot.TryRunWhileCurrent(() => true));
    }

    [Fact]
    public async Task Basic_and_mismatched_route_fail_before_policy_request()
    {
        using var rig = await Rig.CreateAsync();
        rig.Accounts.Current = new Session(rig.Basic, rig.Server);
        Assert.True(rig.Service.ConfigureProvider(Key, rig.Server, PolicyOrigin));
        Assert.Equal(OrganizationPolicyTransportStatus.Unauthorized, (await rig.Service.ReadAsync(Key, CancellationToken.None)).Status);
        rig.Accounts.Current = new Session(rig.Client, rig.Server);
        Assert.True(rig.Service.ConfigureProvider(Key, new Uri("https://wrong.example.org/"), PolicyOrigin));
        Assert.Equal(OrganizationPolicyTransportStatus.Untrusted, (await rig.Service.ReadAsync(Key, CancellationToken.None)).Status);
        Assert.Equal(0, rig.Handler.Count);
    }

    [Fact]
    public async Task Replacement_requires_a_new_route_and_paths_match_exactly()
    {
        using var rig = await Rig.CreateAsync();
        var before = await rig.Service.ReadAsync(Key, CancellationToken.None);
        Assert.NotNull(before.Snapshot);
        rig.Accounts.Current = new Session(rig.Client, rig.Server);
        Assert.Equal(OrganizationPolicyTransportStatus.Stale, (await rig.Service.ReadAsync(Key, CancellationToken.None)).Status);
        Assert.False(before.Snapshot.TryRunWhileCurrent(() => true));
        Assert.True(rig.Service.ConfigureProvider(Key, new Uri(rig.Server, "jmap/"), PolicyOrigin));
        Assert.Equal(OrganizationPolicyTransportStatus.Untrusted, (await rig.Service.ReadAsync(Key, CancellationToken.None)).Status);
        Assert.True(rig.Service.ConfigureProvider(Key, rig.Server, PolicyOrigin));
        Assert.Equal(OrganizationPolicyTransportStatus.Authoritative, (await rig.Service.ReadAsync(Key, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Closing_auth_latch_invalidates_existing_snapshot()
    {
        using var rig = await Rig.CreateAsync();
        var result = await rig.Service.ReadAsync(Key, CancellationToken.None);
        Assert.NotNull(result.Snapshot);
        rig.Client.Auth.Close(new Barbican.Core.Jmap.Errors.AuthError("synthetic refusal", true));
        Assert.False(result.Snapshot.TryRunWhileCurrent(() => true));
        Assert.Equal(OrganizationPolicyTransportStatus.Unauthorized, (await rig.Service.ReadAsync(Key, CancellationToken.None)).Status);
    }

    [Theory]
    [InlineData(true, "example.org", OrganizationPolicyTransportStatus.Public)]
    [InlineData(false, "other.example", OrganizationPolicyTransportStatus.Untrusted)]
    public async Task Public_or_uncovered_responses_never_produce_a_snapshot(bool isPublic, string domains, OrganizationPolicyTransportStatus expected)
    {
        using var rig = await Rig.CreateAsync();
        rig.Handler.IsPublic = isPublic;
        rig.Handler.Domains = domains;
        var result = await rig.Service.ReadAsync(Key, CancellationToken.None);
        Assert.Equal(expected, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Provider_or_account_replacement_during_response_invalidates_read_and_old_snapshot(bool provider)
    {
        using var rig = await Rig.CreateAsync();
        var initial = await rig.Service.ReadAsync(Key, CancellationToken.None);
        Assert.NotNull(initial.Snapshot);
        rig.Handler.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = rig.Service.ReadAsync(Key, CancellationToken.None);
        await rig.Handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        if (provider)
        {
            rig.Service.RemoveProvider(Key);
            Assert.True(rig.Service.ConfigureProvider(Key, rig.Server, PolicyOrigin));
        }
        else
        {
            rig.Accounts.Current = new Session(rig.Client, rig.Server);
            rig.Accounts.Current = new Session(rig.Client, rig.Server);
        }
        Assert.False(initial.Snapshot.TryRunWhileCurrent(() => true));
        rig.Handler.Hold.SetResult();
        var result = await pending;
        Assert.Equal(OrganizationPolicyTransportStatus.Stale, result.Status);
        Assert.Null(result.Snapshot);
    }

    private sealed class Rig : IDisposable
    {
        private OAuthTokenSource? _tokens;
        private OrganizationPolicyTransport? _transport;
        public required JmapClient Client { get; init; }
        public required JmapClient Basic { get; init; }
        public required Snapshots Accounts { get; init; }
        public required Handler Handler { get; init; }
        public required OrganizationPolicyService Service { get; init; }
        public required Uri Server { get; init; }

        public static async Task<Rig> CreateAsync()
        {
            // Obtain a real JMAP session through the in-process server; the policy read uses a separate synthetic OAuth bearer.
            var oauth = new OAuthRig();
            var basic = await FakeJmapHarness.ConnectAsync(oauth.Server, oauth.Time);
            var tokens = new OAuthTokenSource(oauth.OAuth, new MemoryRefreshStore(), oauth.Paths, oauth.Time, Key,
                new OAuthTokens(Access, null, null, oauth.TokenEndpoint, "test-client"));
            var client = new JmapClient(oauth.Http, tokens, basic.Session, new JmapClientOptions(), oauth.Time, JmapTrace.Off);
            var server = new Uri(client.Session.ServerUrl);
            var accounts = new Snapshots(new Session(client, server));
            var handler = new Handler();
            var transport = new OrganizationPolicyTransport(handler);
            var service = new OrganizationPolicyService(accounts, transport);
            Assert.True(service.ConfigureProvider(Key, server, PolicyOrigin));
            return new Rig { _actualOAuth = oauth, _tokens = tokens, _transport = transport, Client = client, Basic = basic, Accounts = accounts, Handler = handler, Service = service, Server = server };
        }
        private OAuthRig? _actualOAuth;
        public void Dispose() { Client.Dispose(); Basic.Dispose(); _tokens?.Dispose(); _transport?.Dispose(); _actualOAuth?.Dispose(); }
    }

    private sealed class Session(JmapClient client, Uri server) : IAccountSession
    {
        public AccountInfo Account { get; } = new(Key, "user@example.org", server.AbsoluteUri);
        public IJmapClient Client { get; } = client;
        public IMailService Mail => throw new NotSupportedException();
        public IMailSync Sync => throw new NotSupportedException();
    }

    private sealed class Snapshots(IAccountSession session) : IAccountSessionSnapshots
    {
        private readonly Lock _gate = new();
        private IAccountSession _current = session;
        public IAccountSession Current { set { lock (_gate) _current = value; } }
        public Result<AccountSessionSnapshot> CaptureActiveSession() { lock (_gate) return Result<AccountSessionSnapshot>.Ok(new(_current, null)); }
        public Result<AccountSessionSnapshot> CaptureOwnerSession(string localAccountKey) => CaptureActiveSession();
        public bool IsSessionCurrent(AccountSessionSnapshot snapshot) { lock (_gate) return ReferenceEquals(snapshot.Session, _current); }
        public bool TryRunWhileCurrent(IReadOnlyList<AccountSessionSnapshot> snapshots, Func<bool> write)
        { lock (_gate) return snapshots.All(x => ReferenceEquals(x.Session, _current)) && write(); }
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int Count { get; private set; }
        public string? Target { get; private set; }
        public string? Server { get; private set; }
        public string? Scheme { get; private set; }
        public string? Token { get; private set; }
        public string Domains { get; set; } = "example.org";
        public bool IsPublic { get; set; }
        public TaskCompletionSource? Hold { get; set; }
        public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            Target = request.RequestUri?.AbsoluteUri;
            Server = request.Headers.GetValues("X-JMAP-Server-Url").Single();
            Scheme = request.Headers.Authorization?.Scheme;
            Token = request.Headers.Authorization?.Parameter;
            if (Hold is { } hold) { Entered.TrySetResult(); await hold.Task.WaitAsync(cancellationToken); }
            var reply = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Policy, Encoding.UTF8, "application/json") };
            reply.Headers.TryAddWithoutValidation("X-Bulwark-Policy-Domains", Domains);
            if (IsPublic) reply.Headers.TryAddWithoutValidation("X-Bulwark-Policy-Scope", "public");
            return reply;
        }
    }
}
