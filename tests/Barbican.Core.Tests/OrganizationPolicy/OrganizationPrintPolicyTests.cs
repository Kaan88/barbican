// Aggregate print permissions over real policy service reads and synthetic OAuth/JMAP sessions.
using System.Net;
using System.Text;
using Barbican.Core.Accounts;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail;
using Barbican.Core.OrganizationPolicy;
using Barbican.Core.Tests.Auth;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.OrganizationPolicy;

public sealed class OrganizationPrintPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnyConnectedRestrictionOrMissingProviderDeniesPrinting()
    {
        using var rig = await Rig.CreateAsync();
        rig.Handler.SecondNoPrint = true;
        Assert.Null(await rig.Print.ReadAsync(Ct));
        rig.Handler.SecondNoPrint = false;
        Assert.NotNull(await rig.Print.ReadAsync(Ct));
        rig.Service.RemoveProvider("second");
        Assert.Null(await rig.Print.ReadAsync(Ct));
        rig.Accounts.ActiveKey = null;
        Assert.Null(await rig.Print.ReadAsync(Ct));
    }

    [Fact]
    public async Task WatermarkUsesActiveAddressAndFinalGateChecksEveryAccountAndRoute()
    {
        using var rig = await Rig.CreateAsync();
        rig.Handler.SecondWatermark = true;
        var active = await rig.Service.ReadAsync("first", Ct);
        Assert.NotNull(active.Snapshot);
        var allowed = await rig.Print.ReadAsync(Ct);
        Assert.NotNull(allowed);
        Assert.Equal(active.Snapshot.AccountAddress, allowed.WatermarkAddress, StringComparer.Ordinal);
        var issued = 0;
        Assert.True(allowed.TryRunWhileCurrent(() => { issued++; return true; }));
        rig.Service.RemoveProvider("second");
        Assert.False(allowed.TryRunWhileCurrent(() => { issued++; return true; }));
        Assert.Equal(1, issued);
    }

    [Fact]
    public async Task MembershipAndActiveSelectionChangesInvalidatePriorAuthorization()
    {
        using var rig = await Rig.CreateAsync();
        var first = await rig.Print.ReadAsync(Ct);
        Assert.NotNull(first);
        rig.Accounts.SecondConnected = false;
        Assert.False(first.TryRunWhileCurrent(() => throw new InvalidOperationException("Changed membership issued.")));
        rig.Accounts.SecondConnected = true;
        var next = await rig.Print.ReadAsync(Ct);
        Assert.NotNull(next);
        rig.Accounts.ActiveKey = "second";
        Assert.False(next.TryRunWhileCurrent(() => throw new InvalidOperationException("Changed active account issued.")));
        rig.Accounts.ActiveKey = "first";
        var priorSession = await rig.Print.ReadAsync(Ct);
        Assert.NotNull(priorSession);
        rig.Accounts.ReplaceSecond();
        Assert.False(priorSession.TryRunWhileCurrent(() => throw new InvalidOperationException("Replaced account session issued.")));
    }

    private sealed class Rig : IDisposable
    {
        private readonly OAuthRig _oauth;
        private readonly OAuthTokenSource _firstToken, _secondToken;
        private readonly JmapClient _basic, _first, _second;
        private readonly OrganizationPolicyTransport _transport;
        private Rig(OAuthRig oauth, OAuthTokenSource firstToken, OAuthTokenSource secondToken, JmapClient basic, JmapClient first, JmapClient second,
            OrganizationPolicyTransport transport, Accounts accounts, Handler handler, OrganizationPolicyService service)
        {
            _oauth = oauth; _firstToken = firstToken; _secondToken = secondToken; _basic = basic; _first = first; _second = second; _transport = transport;
            Accounts = accounts; Handler = handler; Service = service; Print = new(accounts, accounts, service);
        }
        public Accounts Accounts { get; }
        public Handler Handler { get; }
        public OrganizationPolicyService Service { get; }
        public OrganizationPrintPolicy Print { get; }

        public static async Task<Rig> CreateAsync()
        {
            var oauth = new OAuthRig();
            var basic = await FakeJmapHarness.ConnectAsync(oauth.Server, oauth.Time);
            OAuthTokenSource Token(string key) => new(oauth.OAuth, new MemoryRefreshStore(), oauth.Paths, oauth.Time, key,
                new OAuthTokens("synthetic-" + key + "-token-123456", null, null, oauth.TokenEndpoint, "test-client"));
            var oneToken = Token("first"); var twoToken = Token("second");
            var one = new JmapClient(oauth.Http, oneToken, basic.Session, new JmapClientOptions(), oauth.Time, JmapTrace.Off);
            var two = new JmapClient(oauth.Http, twoToken, basic.Session, new JmapClientOptions(), oauth.Time, JmapTrace.Off);
            var accounts = new Accounts(new Session("first", one), new Session("second", two));
            var handler = new Handler();
            var transport = new OrganizationPolicyTransport(handler);
            var service = new OrganizationPolicyService(accounts, transport);
            Assert.True(service.ConfigureProvider("first", new Uri(one.Session.ServerUrl), new Uri("https://policy.example.org/")));
            Assert.True(service.ConfigureProvider("second", new Uri(two.Session.ServerUrl), new Uri("https://policy.example.org/")));
            return new(oauth, oneToken, twoToken, basic, one, two, transport, accounts, handler, service);
        }
        public void Dispose() { _first.Dispose(); _second.Dispose(); _basic.Dispose(); _firstToken.Dispose(); _secondToken.Dispose(); _transport.Dispose(); _oauth.Dispose(); }
    }

    private sealed class Session(string key, JmapClient client) : IAccountSession
    {
        public AccountInfo Account { get; } = new(key, "user@example.org", client.Session.ServerUrl);
        public IJmapClient Client { get; } = client;
        public IMailService Mail => throw new NotSupportedException();
        public IMailSync Sync => throw new NotSupportedException();
    }

    private sealed class Accounts(IAccountSession first, IAccountSession second) : IAccountSessionSnapshots, IAccountOverview
    {
        private readonly Lock _gate = new();
        private string? _active = "first";
        private long _generation = 1;
        private bool _secondConnected = true;
        private IAccountSession _second = second;
        public void ReplaceSecond() { lock (_gate) _second = new Session("second", (JmapClient)_second.Client); }
        public string? ActiveKey { set { lock (_gate) { _active = value; _generation++; } } }
        public bool SecondConnected { set { lock (_gate) _secondConnected = value; } }
        public event EventHandler? StatusesChanged { add { } remove { } }
        public IReadOnlyList<AccountStatus> Statuses
        {
            get
            {
                lock (_gate) return [new(first.Account, string.Equals(_active, "first", StringComparison.Ordinal), true, true, null, null),
                    new(_second.Account, string.Equals(_active, "second", StringComparison.Ordinal), false, _secondConnected, null, null)];
            }
        }
        public Result<AccountSessionSnapshot> CaptureActiveSession()
        {
            lock (_gate) return _active is null ? Missing() : Result<AccountSessionSnapshot>.Ok(
                new(string.Equals(_active, "first", StringComparison.Ordinal) ? first : _second, _generation));
        }
        public Result<AccountSessionSnapshot> CaptureOwnerSession(string key) { lock (_gate) return key switch { "first" => Result<AccountSessionSnapshot>.Ok(new(first, null)), "second" when _secondConnected => Result<AccountSessionSnapshot>.Ok(new(_second, null)), _ => Missing() }; }
        public bool IsSessionCurrent(AccountSessionSnapshot snapshot) { lock (_gate) return Current(snapshot); }
        public bool TryRunWhileCurrent(IReadOnlyList<AccountSessionSnapshot> snapshots, Func<bool> write) { lock (_gate) return snapshots.All(Current) && write(); }
        private bool Current(AccountSessionSnapshot snapshot) => (ReferenceEquals(snapshot.Session, first) || (_secondConnected && ReferenceEquals(snapshot.Session, _second))) &&
            (snapshot.ActiveSelectionGeneration is null || snapshot.ActiveSelectionGeneration == _generation && string.Equals(snapshot.Session.Account.Key, _active, StringComparison.Ordinal));
        private static Result<AccountSessionSnapshot> Missing() => Result<AccountSessionSnapshot>.Fail(new ValidationError("No account."));
    }

    private sealed class Handler : HttpMessageHandler
    {
        public bool SecondNoPrint { get; set; }
        public bool SecondWatermark { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var second = request.Headers.Authorization?.Parameter?.Contains("second", StringComparison.Ordinal) == true;
            var body = "{\"organisation\":{\"devices\":{\"noPrint\":" + (second && SecondNoPrint ? "true" : "false") +
                ",\"watermark\":" + (second && SecondWatermark ? "true" : "false") + "}},\"scopes\":[]}";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.TryAddWithoutValidation("X-Bulwark-Policy-Domains", "example.org");
            return Task.FromResult(response);
        }
    }
}
