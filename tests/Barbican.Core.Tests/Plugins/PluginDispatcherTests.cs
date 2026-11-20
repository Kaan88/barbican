using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail;
using Barbican.Core.Plugins.Api.RawSend;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins;

public sealed class PluginDispatcherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static PluginPackageIdentity Package(char hash = 'a') => new("probe", "1.0.0", new string(hash, 64));
    private static PluginGrantSnapshot Grant(long generation = 1, bool enabled = true, char hash = 'a', string[]? permissions = null, string[]? accounts = null) =>
        new(Package(hash), generation, enabled, permissions ?? ["email:read"], accounts ?? ["a", "b"]);
    private static PluginValue EmptyArgs() { var b = new PluginValueBuilder(); return b.Build(b.Array()); }
    private static PluginApiRequest Request(string method = "probe.read") => new("id", method, EmptyArgs());
    private static PluginCallOutcome Text(string value) { var b = new PluginValueBuilder(); return PluginCallOutcome.Succeeded(b.Build(b.String(value))); }

    [Theory]
    [InlineData(false, 1, 'a')]
    [InlineData(true, 2, 'a')]
    [InlineData(true, 1, 'b')]
    public async Task DisabledRegrantedAndChangedContentNeverReachAdapter(bool enabled, long generation, char hash)
    {
        var authority = new Authority(Grant(generation, enabled, hash));
        var calls = 0;
        var method = new Method((_, _) => { calls++; return Task.FromResult(PluginCallOutcome.Succeeded()); });
        var dispatcher = new PluginApiDispatcher("auto-dispatch", [method], authority, new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-dispatch", Grant(), PluginFrameKind.Background);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await dispatcher.DispatchAsync(binding, Request(), Ct)).Error);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ExactPermissionAlternativesAndPrivilegedTierAreEnforcedBeforeInvocation()
    {
        var authority = new Authority(Grant(permissions: ["email:read-more"]));
        var method = new Method((_, _) => throw new InvalidOperationException("must not invoke"));
        var dispatcher = new PluginApiDispatcher("auto-dispatch", [method], authority, new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-dispatch", Grant(), PluginFrameKind.Background);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await dispatcher.DispatchAsync(binding, Request(), Ct)).Error);
        authority.Current = Grant();
        method.Descriptor = new("probe.read", [["email:read"]], TimeSpan.FromSeconds(5), privilegedOnly: true);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await dispatcher.DispatchAsync(binding, Request(), Ct)).Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawSendGrantAloneCannotReachEitherRawHostMethod(bool submitOnly)
    {
        var grant = Grant(permissions: ["email:raw-send"]);
        var host = new UnreachableRawHost();
        var method = submitOnly ? PluginRawSendMethods.SubmitRaw(host) : PluginRawSendMethods.SendRaw(host);
        var dispatcher = new PluginApiDispatcher("auto-dispatch", [method], new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-dispatch", grant, PluginFrameKind.Background);
        var values = new PluginValueBuilder();
        var args = values.Build(values.Array(values.Buffer("synthetic"u8), values.String("identity")));
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("id", method.Descriptor.Name, args), Ct);

        Assert.Equal(PluginErrorCode.PermissionDenied, result.Error);
        Assert.Equal(0, host.Calls);
    }

    private sealed class UnreachableRawHost : IPluginRawSendHost
    {
        public int Calls { get; private set; }

        public Task<PluginRawSendOutcome> SendAsync(IPluginInvocationContext context, PluginRawSendRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new PluginRawSendOutcome(null, PluginErrorCode.Failed));
        }
    }

    [Fact]
    public async Task ForeignClosedAndUnknownCallsNeverReachAuthorityOrAdapter()
    {
        var authority = new Authority(Grant());
        var dispatcher = new PluginApiDispatcher("auto-dispatch", [], authority, new Sessions());
        await using var foreign = PluginRuntimeBinding.Create("auto-other", Grant(), PluginFrameKind.Background);
        Assert.Equal(PluginErrorCode.StaleContext, (await dispatcher.DispatchAsync(foreign, Request(), Ct)).Error);
        await using var binding = PluginRuntimeBinding.Create("auto-dispatch", Grant(), PluginFrameKind.Background);
        Assert.Equal(PluginErrorCode.UnknownMethod, (await dispatcher.DispatchAsync(binding, Request(), Ct)).Error);
        await binding.DisposeAsync();
        Assert.Equal(PluginErrorCode.StaleContext, (await dispatcher.DispatchAsync(binding, Request(), Ct)).Error);
        Assert.Equal(0, authority.Reads);
    }

    [Fact]
    public async Task BackgroundFollowsCallTimeAccountButEachCallKeepsItsCapturedSession()
    {
        var sessions = new Sessions();
        var method = new Method((context, _) => Task.FromResult(Text(context.CaptureActive().Value!.LocalAccountKey)));
        var dispatcher = new PluginApiDispatcher("auto-dispatch", [method], new Authority(Grant()), sessions);
        await using var binding = PluginRuntimeBinding.Create("auto-dispatch", Grant(), PluginFrameKind.Background);
        Assert.Equal("a", (await dispatcher.DispatchAsync(binding, Request(), Ct)).Value!.StringValue);
        sessions.Switch("b");
        Assert.Equal("b", (await dispatcher.DispatchAsync(binding, Request(), Ct)).Value!.StringValue);
        Assert.True(binding.IsAlive);
    }

    [Theory]
    [InlineData(false, PluginErrorCode.StaleContext)]
    [InlineData(true, PluginErrorCode.UnknownOutcome)]
    public async Task QueuedResultRetainsItsCaptureAfterInvocationCloses(bool issue, PluginErrorCode expected)
    {
        var sessions = new Sessions();
        var method = new Method(async (context, ct) =>
        {
            var active = context.CaptureActive().Value!;
            if (issue)
            {
                Assert.True(await context.CheckCurrentAsync(ct));
                context.MarkMutationIssued();
            }

            return Text(active.LocalAccountKey);
        }, mayMutate: issue);
        var dispatcher = new PluginApiDispatcher("auto-dispatch", [method], new Authority(Grant()), sessions);
        await using var binding = PluginRuntimeBinding.Create("auto-dispatch", Grant(), PluginFrameKind.Background);

        var first = await dispatcher.DispatchAsync(binding, Request(), Ct);
        Assert.True(first.IsSuccess);
        Assert.True(first.TryWriteForDelivery(binding, Grant(), () => Assert.Equal("a", first.Value!.StringValue)));
        sessions.Switch("b");
        Assert.False(first.TryWriteForDelivery(binding, Grant(), () => throw new InvalidOperationException("Stale reply posted.")));
        var rejected = first.RefuseDelivery();
        Assert.Equal(expected, rejected.Error);
        Assert.Null(rejected.Value);

        var next = await dispatcher.DispatchAsync(binding, Request(), Ct);
        Assert.True(next.TryWriteForDelivery(binding, Grant(), () => Assert.Equal("b", next.Value!.StringValue)));
    }

    [Fact]
    public async Task QueuedResultWithoutAnAccountStillRequiresItsOriginalLiveGrantAndBinding()
    {
        var authority = new Authority(Grant());
        var dispatcher = new PluginApiDispatcher("auto-dispatch", [new Method((_, _) => Task.FromResult(Text("value")))], authority, new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-dispatch", Grant(), PluginFrameKind.Background);
        await using var sibling = PluginRuntimeBinding.Create("auto-dispatch", Grant(), PluginFrameKind.Background);
        var outcome = await dispatcher.DispatchAsync(binding, Request(), Ct);
        Assert.True(outcome.TryWriteForDelivery(binding, Grant(), () => { }));
        Assert.False(outcome.TryWriteForDelivery(sibling, Grant(), () => throw new InvalidOperationException("Sibling reply posted.")));
        authority.Current = Grant(2);
        Assert.False(outcome.TryWriteForDelivery(binding, Grant(2), () => throw new InvalidOperationException("Old grant replied.")));
    }

    [Fact]
    public async Task UnfencedSuccessCannotPassAHostDeliveryCheck()
    {
        await using var binding = PluginRuntimeBinding.Create("auto-dispatch", Grant(), PluginFrameKind.Background);
        var outcome = Text("adapter-only");
        Assert.False(outcome.TryWriteForDelivery(binding, Grant(), () => throw new InvalidOperationException("Unfenced reply posted.")));
        Assert.Equal(PluginErrorCode.StaleContext, outcome.RefuseDelivery().Error);
    }

    [Fact]
    public async Task ActiveAccountAbaInvalidatesResultWithoutChangingCapturedClient()
    {
        var sessions = new Sessions();
        var method = new Method((context, _) =>
        {
            var first = context.CaptureActive().Value!;
            sessions.Switch("b"); sessions.Switch("a");
            Assert.Same(first, context.CaptureActive().Value);
            return Task.FromResult(Text("must not disclose stale result"));
        });
        var dispatcher = new PluginApiDispatcher("auto-dispatch", [method], new Authority(Grant()), sessions);
        await using var binding = PluginRuntimeBinding.Create("auto-dispatch", Grant(), PluginFrameKind.Background);
        var outcome = await dispatcher.DispatchAsync(binding, Request(), Ct);
        Assert.Equal(PluginErrorCode.StaleContext, outcome.Error);
        Assert.Null(outcome.Value);
    }

    [Fact]
    public async Task SwitchingWhileAdapterAwaitsDropsItsResultAndPreventsLateWrite()
    {
        var sessions = new Sessions();
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        var method = new Method(async (context, ct) =>
        {
            var session = context.CaptureActive().Value!;
            captured.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            Assert.Equal("a", session.LocalAccountKey, StringComparer.Ordinal);
            Assert.False(await context.CheckCurrentAsync(ct));
            Assert.Throws<InvalidOperationException>(context.MarkMutationIssued);
            if (context.HasIssuedMutation) writes++;
            return Text("old account data");
        }, mayMutate: true);
        var dispatcher = new PluginApiDispatcher("auto-dispatch", [method], new Authority(Grant()), sessions);
        await using var binding = PluginRuntimeBinding.Create("auto-dispatch", Grant(), PluginFrameKind.Background);
        var pending = dispatcher.DispatchAsync(binding, Request(), Ct);
        await captured.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        sessions.Switch("b");
        release.SetResult();
        Assert.Equal(PluginErrorCode.StaleContext, (await pending).Error);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task ResourceOwnerCanBeNonActiveAndUnconsentedLoginIsNeverResolved()
    {
        var sessions = new Sessions();
        var authority = new Authority(Grant(accounts: ["b"]));
        var method = new Method((context, _) =>
        {
            Assert.False(context.CaptureOwner("a").IsOk);
            Assert.False(context.CaptureActive().IsOk);
            var owner = context.CaptureOwner("b");
            Assert.True(owner.IsOk);
            Assert.Null(owner.Value!.ActiveSelectionGeneration);
            return Task.FromResult(Text(owner.Value.LocalAccountKey));
        });
        var dispatcher = new PluginApiDispatcher("auto-dispatch", [method], authority, sessions);
        await using var binding = PluginRuntimeBinding.Create("auto-dispatch", authority.Current!, PluginFrameKind.Background);
        Assert.Equal("b", (await dispatcher.DispatchAsync(binding, Request(), Ct)).Value!.StringValue);
        Assert.Equal("b", Assert.Single(sessions.OwnerLookups), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(false, PluginErrorCode.StaleContext)]
    [InlineData(true, PluginErrorCode.UnknownOutcome)]
    public async Task RevocationSuppressesResultsAndDistinguishesAlreadyIssuedMutation(bool issue, PluginErrorCode expected)
    {
        var authority = new Authority(Grant());
        var writes = 0;
        var method = new Method(async (context, ct) =>
        {
            Assert.True(context.CaptureActive().IsOk);
            if (issue)
            {
                Assert.True(await context.CheckCurrentAsync(ct));
                context.MarkMutationIssued();
                writes++;
            }
            authority.Current = Grant(2);
            Assert.False(await context.CheckCurrentAsync(ct));
            Assert.Throws<InvalidOperationException>(context.MarkMutationIssued);
            return Text("stale");
        }, mayMutate: true);
        var dispatcher = new PluginApiDispatcher("auto-dispatch", [method], authority, new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-dispatch", Grant(), PluginFrameKind.Background);
        var outcome = await dispatcher.DispatchAsync(binding, Request(), Ct);
        Assert.Equal(expected, outcome.Error);
        Assert.Null(outcome.Value);
        Assert.Equal(issue ? 1 : 0, writes);
    }

    [Fact]
    public async Task RevocationNotificationBetweenLastCheckAndMutationClosesTheWriteGate()
    {
        var authority = new Authority(Grant());
        var writes = 0;
        var method = new Method(async (context, ct) =>
        {
            Assert.True(await context.CheckCurrentAsync(ct));
            authority.Current = Grant(2);
            Assert.Throws<InvalidOperationException>(context.MarkMutationIssued);
            if (context.HasIssuedMutation) writes++;
            return Text("must be dropped");
        }, mayMutate: true);
        var dispatcher = new PluginApiDispatcher("auto-dispatch", [method], authority, new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-dispatch", Grant(), PluginFrameKind.Background);
        Assert.Equal(PluginErrorCode.StaleContext, (await dispatcher.DispatchAsync(binding, Request(), Ct)).Error);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task NativeExceptionDetailsAreNeverReturned()
    {
        var method = new Method((_, _) => throw new InvalidOperationException("synthetic-private-secret"));
        var dispatcher = new PluginApiDispatcher("auto-dispatch", [method], new Authority(Grant()), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-dispatch", Grant(), PluginFrameKind.Background);
        var outcome = await dispatcher.DispatchAsync(binding, Request(), Ct);
        Assert.Equal(PluginErrorCode.Failed, outcome.Error);
        Assert.DoesNotContain("synthetic-private-secret", outcome.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimedOutWorkRetainsConcurrencyQuotaUntilItActuallyStops()
    {
        var time = new FakeTimeProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IPluginInvocationContext? captured = null;
        var method = new Method(async (context, _) =>
        {
            captured = context;
            started.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
            Assert.Throws<InvalidOperationException>(context.MarkMutationIssued);
            return Text("late");
        }, mayMutate: true, deadline: TimeSpan.FromSeconds(1));
        var dispatcher = new PluginApiDispatcher("auto-dispatch", [method], new Authority(Grant()), new Sessions(), time, maxPerInstance: 1);
        await using var binding = PluginRuntimeBinding.Create("auto-dispatch", Grant(), PluginFrameKind.Background);
        var work = dispatcher.DispatchAsync(binding, Request(), Ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(PluginErrorCode.Timeout, (await work.WaitAsync(TimeSpan.FromSeconds(5), Ct)).Error);
        Assert.NotNull(captured);
        Assert.False(await captured.CheckCurrentAsync(Ct));
        Assert.Equal(PluginErrorCode.RateLimited, (await dispatcher.DispatchAsync(binding, Request(), Ct)).Error);
        release.SetResult();
    }

    private sealed class Method(Func<IPluginInvocationContext, CancellationToken, Task<PluginCallOutcome>> invoke, bool mayMutate = false, TimeSpan? deadline = null) : IPluginApiMethod
    {
        public PluginApiDescriptor Descriptor { get; set; } = new("probe.read", [["email:read"]], deadline ?? TimeSpan.FromSeconds(5), mayMutate: mayMutate);
        public PluginErrorCode? ValidateArguments(PluginValue arguments) => arguments.Kind == PluginValueKind.Array ? null : PluginErrorCode.InvalidArguments;
        public Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct) => invoke(context, ct);
    }
    private sealed class Authority(PluginGrantSnapshot? initial) : IPluginAuthority
    {
        private PluginGrantSnapshot? _current = initial;
        public int Reads { get; private set; }
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed;
        public PluginGrantSnapshot? Current
        {
            get => _current;
            set { _current = value; Changed?.Invoke(this, new PluginAuthorityChangedEventArgs("probe", value?.Generation ?? 1)); }
        }
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) { Reads++; return Task.FromResult(Result<PluginGrantSnapshot?>.Ok(Current)); }
    }
    private sealed class Sessions : IPluginSessionSource
    {
        private string _active = "a";
        private long _epoch = 1;
        private readonly Dictionary<string, IAccountSession> _sessions = new(StringComparer.Ordinal) { ["a"] = new Session("a"), ["b"] = new Session("b") };
        public List<string> OwnerLookups { get; } = [];
        public void Switch(string key) { _active = key; _epoch++; }
        public Result<PluginSessionCapture> CaptureActive() => Result<PluginSessionCapture>.Ok(new(_sessions[_active], _epoch));
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey)
        {
            OwnerLookups.Add(localAccountKey);
            return _sessions.TryGetValue(localAccountKey, out var session) ? Result<PluginSessionCapture>.Ok(new(session, null)) : Result<PluginSessionCapture>.Fail(new ValidationError("Unknown login."));
        }
        public bool TryRunWhileCurrent(IReadOnlyList<PluginSessionCapture> captures, Func<bool> write) =>
            captures.All(IsCurrent) && write();
        public bool IsCurrent(PluginSessionCapture capture) => _sessions.TryGetValue(capture.LocalAccountKey, out var session) && ReferenceEquals(session, capture.Session)
            && (capture.ActiveSelectionGeneration is null || capture.ActiveSelectionGeneration == _epoch);
    }
    private sealed class Session(string key) : IAccountSession
    {
        public AccountInfo Account { get; } = new(key, "sample@example.org", "https://jmap.example.org");
        public IJmapClient Client => throw new NotSupportedException("No backend accessed in fence tests.");
        public IMailService Mail => throw new NotSupportedException("No backend accessed in fence tests.");
        public IMailSync Sync => throw new NotSupportedException("No backend accessed in fence tests.");
    }
}
