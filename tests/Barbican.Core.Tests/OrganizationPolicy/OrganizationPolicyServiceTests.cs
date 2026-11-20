using System.Net;
using System.Security.Cryptography;
using System.Text;
using Barbican.Core.Accounts;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail;
using Barbican.Core.OrganizationPolicy;
using Barbican.Core.Plugins.Packages.Managed;
using Barbican.Core.Tests.Auth;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.OrganizationPolicy;

public sealed class OrganizationPolicyServiceTests
{
    private const string Key = "sample";
    private const string Access = "synthetic-access-token-123";
    private static readonly Uri PolicyOrigin = new("https://policy.example.org/");
    private const string Policy = """{"organisation":{"devices":{"noPrint":false}},"scopes":[{"id":"example","match":{"domains":["example.org"]},"policy":{"organisation":{"devices":{"noPrint":true}}}}]}""";
    private static readonly string ManagedKey = Convert.ToBase64String(Convert.FromHexString("3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c"));

    [Fact]
    public async Task Existing_organization_policy_consent_does_not_approve_managed_registry_credentials()
    {
        using var rig = await Rig.CreateAsync();
        var handler = new ManagedContextHandler();
        using var transport = new ManagedPluginTransport(handler);
        var source = new ManagedPluginSource(rig.Accounts, transport);
        var ct = TestContext.Current.CancellationToken;
        Assert.False((await source.FetchAsync(Key, "managed-probe", "1.21.2", ct)).IsOk);
        Assert.Equal(0, handler.Calls);

        var original = rig.Accounts.Active();
        rig.Accounts.Current = new Session(rig.Client, rig.Server);
        Assert.False(source.ApproveProvider(original, PolicyOrigin, ManagedKey));
        Assert.True(source.ApproveProvider(rig.Accounts.Active(), PolicyOrigin, ManagedKey));
        rig.Accounts.Current = original.Session;
        Assert.False((await source.FetchAsync(Key, "managed-probe", "1.21.2", ct)).IsOk);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Managed_provider_disconnect_blocks_registry_request_after_context_creation()
    {
        using var rig = await Rig.CreateAsync();
        var handler = new ManagedContextHandler();
        using var transport = new ManagedPluginTransport(handler);
        var source = new ManagedPluginSource(rig.Accounts, transport);
        var selected = rig.Accounts.Active();
        Assert.False(source.ApproveProvider(selected, PolicyOrigin, "invalid-key"));
        Assert.True(source.ApproveProvider(selected, PolicyOrigin, ManagedKey));
        var ct = TestContext.Current.CancellationToken;
        var pending = source.FetchAsync(Key, "managed-probe", "1.21.2", ct);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.True(source.Disconnect(selected));
        handler.Release.SetResult();

        Assert.False((await pending).IsOk);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Managed_review_cancellation_or_host_revocation_before_request_sends_no_credential(bool cancel)
    {
        using var rig = await Rig.CreateAsync();
        var handler = new ManagedContextHandler();
        using var source = new ManagedPluginSource(rig.Accounts, new ManagedPluginTransport(handler));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var handoffs = 0;
        bool HostFence(Func<bool> issue)
        {
            if (++handoffs == 1) return issue();
            if (!cancel) return false;
            stop.Cancel();
            return issue();
        }
        Task<Result<ManagedPluginCandidate>> FetchAsync() => source.AcquireForReviewAsync(rig.Accounts.Active(), PolicyOrigin, ManagedKey,
            "managed-probe", "1.21.2", HostFence, stop.Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FetchAsync());
        else Assert.False((await FetchAsync()).IsOk);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ActivationRequiresExactApprovedOriginSignerAndCurrentOwnerBeforeAnyRequest()
    {
        using var rig = await Rig.CreateAsync();
        var handler = new ManagedContextHandler();
        using var source = new ManagedPluginSource(rig.Accounts, new ManagedPluginTransport(handler));
        var ct = TestContext.Current.CancellationToken;
        var digest = Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(ManagedKey)));
        Task<Result<ManagedPluginCandidate>> FetchAsync(Uri origin, string signer) => source.AcquireForActivationAsync(
            Key, origin, signer, "managed-probe", "1.21.2", static issue => issue(), ct);
        Assert.False((await FetchAsync(PolicyOrigin, digest)).IsOk);
        Assert.True(source.ApproveProvider(rig.Accounts.Active(), PolicyOrigin, ManagedKey));
        Assert.False((await FetchAsync(new Uri("https://other.example.org/"), digest)).IsOk);
        Assert.False((await FetchAsync(PolicyOrigin, new string('0', 64))).IsOk);
        var old = rig.Accounts.Session;
        rig.Accounts.Current = new Session(rig.Client, rig.Server);
        Assert.False((await FetchAsync(PolicyOrigin, digest)).IsOk);
        rig.Accounts.Current = old;
        Assert.False((await source.AcquireForActivationAsync(Key, PolicyOrigin, digest, "managed-probe", "1.21.2", static _ => false, ct)).IsOk);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ActivationStopsAtRevokedRouteWhileContextRequestIsInFlight()
    {
        using var rig = await Rig.CreateAsync();
        var handler = new ManagedContextHandler();
        using var source = new ManagedPluginSource(rig.Accounts, new ManagedPluginTransport(handler));
        var selection = rig.Accounts.Active();
        Assert.True(source.ApproveProvider(selection, PolicyOrigin, ManagedKey));
        var digest = Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(ManagedKey)));
        var ct = TestContext.Current.CancellationToken;
        var pending = source.AcquireForActivationAsync(Key, PolicyOrigin, digest, "managed-probe", "1.21.2", static issue => issue(), ct);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.True(source.Disconnect(selection));
        handler.Release.SetResult();
        Assert.False((await pending).IsOk);
        Assert.Equal(1, handler.Calls);
    }
    private sealed class ManagedContextHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri(PolicyOrigin, "/api/auth/stalwart-context"), request.RequestUri);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            var reply = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true}""") };
            reply.Headers.TryAddWithoutValidation("Set-Cookie", "jmap_stalwart_ctx=synthetic-registry-context; Path=/; Secure; HttpOnly");
            return reply;
        }
    }

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
    public async Task PolicyRefreshCanOverlapManagedAccountFencedDelivery()
    {
        using var rig = await Rig.CreateAsync();
        var ct = TestContext.Current.CancellationToken;
        var captured = rig.Accounts.Active();
        var initial = await rig.Service.ReadAsync(captured, ct);
        Assert.NotNull(initial.Snapshot);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var refreshAtAccountFence = new ManualResetEventSlim();

        var delivery = Task.Run(() => rig.Accounts.TryRunWhileCurrent([captured], () =>
        {
            var owner = Environment.CurrentManagedThreadId;
            rig.Accounts.BeforeFence = () =>
            {
                if (Environment.CurrentManagedThreadId != owner) refreshAtAccountFence.Set();
            };
            held.SetResult();
            return refreshAtAccountFence.Wait(TimeSpan.FromSeconds(5), ct) &&
                initial.Snapshot.TryRunWhileCurrent(static () => true);
        }), ct);
        await held.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        var refresh = Task.Run(() => rig.Service.ReadAsync(captured, ct), ct);
        Assert.True(await delivery.WaitAsync(TimeSpan.FromSeconds(8), ct));
        Assert.Equal(OrganizationPolicyTransportStatus.Authoritative, (await refresh.WaitAsync(TimeSpan.FromSeconds(8), ct)).Status);
        rig.Accounts.BeforeFence = null;
    }

    [Fact]
    public async Task Basic_enrollment_is_refused_before_policy_request()
    {
        using var rig = await Rig.CreateAsync();
        rig.Accounts.Current = new Session(rig.Basic, rig.Server);
        Assert.False(rig.Service.ConfigureProvider(rig.Accounts.Active(), PolicyOrigin));
        Assert.Equal(0, rig.Handler.Count);
    }

    [Fact]
    public async Task Replacement_requires_explicit_reenrollment()
    {
        using var rig = await Rig.CreateAsync();
        var before = await rig.Service.ReadAsync(Key, CancellationToken.None);
        Assert.NotNull(before.Snapshot);
        rig.Accounts.Current = new Session(rig.Client, rig.Server);
        Assert.Equal(OrganizationPolicyTransportStatus.Stale, (await rig.Service.ReadAsync(Key, CancellationToken.None)).Status);
        Assert.False(before.Snapshot.TryRunWhileCurrent(() => true));
        var replacement = rig.Accounts.Active();
        Assert.Null(rig.Service.ProviderFor(replacement));
        Assert.True(rig.Service.ConfigureProvider(replacement, PolicyOrigin));
        Assert.Equal(OrganizationPolicyTransportStatus.Authoritative, (await rig.Service.ReadAsync(Key, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Stale_active_selection_cannot_change_or_remove_current_provider_after_aba()
    {
        using var rig = await Rig.CreateAsync();
        var first = rig.Accounts.Active();
        var original = rig.Accounts.Session;
        rig.Accounts.Current = new Session(rig.Client, rig.Server);
        rig.Accounts.Current = original;
        var current = rig.Accounts.Active();
        Assert.False(rig.Service.ConfigureProvider(first, new Uri("https://other.example.org/")));
        Assert.False(rig.Service.RemoveProvider(first));
        Assert.Equal(PolicyOrigin, rig.Service.ProviderFor(current));
        Assert.True(rig.Service.RemoveProvider(current));
        Assert.Null(rig.Service.ProviderFor(current));
    }

    [Theory]
    [InlineData("http://policy.example.org/")]
    [InlineData("https://user@policy.example.org/")]
    [InlineData("https://policy.example.org/path")]
    [InlineData("https://policy.example.org/?next=other")]
    [InlineData("https://policy.example.org/#fragment")]
    public async Task Invalid_provider_origin_does_not_replace_existing_route(string candidate)
    {
        using var rig = await Rig.CreateAsync();
        var current = rig.Accounts.Active();
        Assert.False(rig.Service.ConfigureProvider(current, new Uri(candidate)));
        Assert.Equal(PolicyOrigin, rig.Service.ProviderFor(current));
        Assert.Equal(OrganizationPolicyTransportStatus.Authoritative, (await rig.Service.ReadAsync(Key, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Disconnected_account_cannot_use_or_change_a_route()
    {
        using var rig = await Rig.CreateAsync();
        var current = rig.Accounts.Active();
        var existing = await rig.Service.ReadAsync(Key, CancellationToken.None);
        Assert.NotNull(existing.Snapshot);
        rig.Accounts.Disconnect();
        Assert.Null(rig.Service.ProviderFor(current));
        Assert.False(rig.Service.RemoveProvider(current));
        Assert.False(rig.Service.ConfigureProvider(current, PolicyOrigin));
        Assert.False(existing.Snapshot.TryRunWhileCurrent(() => true));
    }

    [Fact]
    public async Task Selected_read_refuses_stale_generation_before_transport()
    {
        using var rig = await Rig.CreateAsync();
        var selected = rig.Accounts.Active();
        var original = rig.Accounts.Session;
        rig.Accounts.Current = new Session(rig.Client, rig.Server);
        rig.Accounts.Current = original;
        var result = await rig.Service.ReadAsync(selected, CancellationToken.None);
        Assert.Equal(OrganizationPolicyTransportStatus.Stale, result.Status);
        Assert.Equal(0, rig.Handler.Count);
    }

    [Fact]
    public async Task Selected_read_refuses_response_after_selection_changes_and_returns()
    {
        using var rig = await Rig.CreateAsync();
        var selected = rig.Accounts.Active();
        var original = rig.Accounts.Session;
        rig.Handler.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = rig.Service.ReadAsync(selected, CancellationToken.None);
        await rig.Handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        rig.Accounts.Current = new Session(rig.Client, rig.Server);
        rig.Accounts.Current = original;
        rig.Handler.Hold.SetResult();
        var result = await pending;
        Assert.Equal(OrganizationPolicyTransportStatus.Stale, result.Status);
        Assert.Null(result.Snapshot);
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
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failed_verification_preserves_existing_route_or_leaves_none(bool initiallyConfigured)
    {
        using var rig = await Rig.CreateAsync();
        var selected = rig.Accounts.Active();
        if (!initiallyConfigured) Assert.True(rig.Service.RemoveProvider(selected));
        rig.Handler.IsPublic = true;
        var result = await rig.Service.VerifyProviderAsync(selected, new Uri("https://candidate.example.org/"), CancellationToken.None);
        Assert.Equal(OrganizationPolicyTransportStatus.Public, result.Status);
        Assert.Null(result.Snapshot);
        Assert.Equal(initiallyConfigured ? PolicyOrigin : null, rig.Service.ProviderFor(selected));
    }

    [Fact]
    public async Task First_successful_promotion_wins_over_older_pending_candidate()
    {
        using var rig = await Rig.CreateAsync();
        var selected = rig.Accounts.Active();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Handler.Hold = gate;
        var first = rig.Service.VerifyProviderAsync(selected, new Uri("https://first.example.org/"), CancellationToken.None);
        await rig.Handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        rig.Handler.Hold = null;
        var winner = await rig.Service.VerifyProviderAsync(selected, new Uri("https://winner.example.org/"), CancellationToken.None);
        Assert.Equal(OrganizationPolicyTransportStatus.Authoritative, winner.Status);
        Assert.NotNull(winner.Snapshot);
        Assert.True(winner.Snapshot.TryRunWhileCurrent(() => true));
        gate.SetResult();
        var loser = await first;
        Assert.Equal(OrganizationPolicyTransportStatus.Stale, loser.Status);
        Assert.Null(loser.Snapshot);
        Assert.Equal(new Uri("https://winner.example.org/"), rig.Service.ProviderFor(selected));
        Assert.True(winner.Snapshot.TryRunWhileCurrent(() => true));
    }

    [Fact]
    public async Task Selection_change_during_candidate_read_prevents_promotion()
    {
        using var rig = await Rig.CreateAsync();
        var selected = rig.Accounts.Active();
        var original = rig.Accounts.Session;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Handler.Hold = gate;
        var pending = rig.Service.VerifyProviderAsync(selected, new Uri("https://candidate.example.org/"), CancellationToken.None);
        await rig.Handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        rig.Accounts.Current = new Session(rig.Client, rig.Server);
        rig.Accounts.Current = original;
        gate.SetResult();
        var result = await pending;
        Assert.Equal(OrganizationPolicyTransportStatus.Stale, result.Status);
        Assert.Null(result.Snapshot);
        Assert.Equal(PolicyOrigin, rig.Service.ProviderFor(rig.Accounts.Active()));
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
            var active = rig.Accounts.Active();
            Assert.True(rig.Service.RemoveProvider(active));
            Assert.True(rig.Service.ConfigureProvider(active, PolicyOrigin));
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
            Assert.True(service.ConfigureProvider(accounts.Active(), PolicyOrigin));
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
        private IAccountSession? _current = session;
        private long _generation = 1;
        public Action? BeforeFence { get; set; }
        public IAccountSession Session { get { lock (_gate) return _current!; } }
        public IAccountSession Current { set { lock (_gate) { _current = value; _generation++; } } }
        public void Disconnect() { lock (_gate) { _current = null; _generation++; } }
        public AccountSessionSnapshot Active() => CaptureActiveSession().Value!;
        public Result<AccountSessionSnapshot> CaptureActiveSession()
        {
            lock (_gate) return _current is null ? Result<AccountSessionSnapshot>.Fail(new Barbican.Core.Jmap.Errors.ValidationError("No account.")) : Result<AccountSessionSnapshot>.Ok(new(_current, _generation));
        }
        public Result<AccountSessionSnapshot> CaptureOwnerSession(string localAccountKey)
        {
            lock (_gate) return _current is null ? Result<AccountSessionSnapshot>.Fail(new Barbican.Core.Jmap.Errors.ValidationError("No account.")) : Result<AccountSessionSnapshot>.Ok(new(_current, null));
        }
        private bool CurrentSnapshot(AccountSessionSnapshot snapshot) => ReferenceEquals(snapshot.Session, _current) &&
            (snapshot.ActiveSelectionGeneration is null || snapshot.ActiveSelectionGeneration == _generation);
        public bool IsSessionCurrent(AccountSessionSnapshot snapshot) { lock (_gate) return CurrentSnapshot(snapshot); }
        public bool TryRunWhileCurrent(IReadOnlyList<AccountSessionSnapshot> snapshots, Func<bool> write)
        {
            BeforeFence?.Invoke();
            if (!_gate.TryEnter(TimeSpan.FromSeconds(4))) return false;
            try { return snapshots.All(CurrentSnapshot) && write(); }
            finally { _gate.Exit(); }
        }
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
