// Source: lib/__tests__/plugin-host-checker.test.ts ("lets a plugin with compose:read check when the user asks, never while typing", "routes on-demand text and results
// to the composer it names", "forgets a plugin's checkers when it is turned off"), lib/plugin-sandbox/host-api-checker.ts:78-101.
using System.Text.Json;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Editor;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
using Barbican.Core.Plugins.Api.Composer;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class ComposerCheckAcceptanceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly PluginPackageIdentity Package = new("probe", "1.0.0", new string('a', 64));
    private static PluginGrantSnapshot Grant(params string[] permissions) => new(Package, 1, true, permissions, ["a"]);

    private sealed class Fixture
    {
        public Fixture(PluginGrantSnapshot grant)
        {
            Grant = grant;
            Registry = new PluginCheckerRegistry(TimeProvider.System);
            Limiter = PluginComposerEditRateLimiter.Create(TimeProvider.System, 10);
            Dispatcher = Create(grant);
        }

        public PluginGrantSnapshot Grant { get; }
        public PluginCheckerRegistry Registry { get; }
        public PluginAnnotationStore Store { get; } = new();
        public IPluginComposerEditRateLimiter Limiter { get; }
        public Access Access { get; } = new();
        public PluginApiDispatcher Dispatcher { get; }

        public PluginApiDispatcher Create(PluginGrantSnapshot grant)
        {
            Access.Store = Store;
            return new("auto-check", [.. Registry.Methods, PluginComposerCheckMethods.GetCheckText(Access, Registry, Limiter),
                PluginComposerCheckMethods.SetAnnotations(Access, Registry, Limiter)], new Authority(grant), new Sessions());
        }

        public async Task<PluginCallOutcome> CallAsync(PluginRuntimeBinding binding, string method, string json, PluginApiDispatcher? dispatcher = null)
        {
            using var doc = JsonDocument.Parse(json);
            return await (dispatcher ?? Dispatcher).DispatchAsync(binding, new PluginApiRequest("request", "composer." + method, PluginValue.FromJson(doc.RootElement)), Ct);
        }

        public Task<PluginCallOutcome> RegisterAsync(PluginRuntimeBinding binding, string id, string mode = "on-demand") =>
            CallAsync(binding, "registerChecker", $$"""[{"id":"{{id}}","label":"Check {{id}}","kinds":["spelling","grammar"],"mode":"{{mode}}"}]""");

        public static PluginRuntimeBinding Binding(PluginGrantSnapshot grant) => PluginRuntimeBinding.Create("auto-check", grant, PluginFrameKind.Background);

        public async Task<(string Revision, string Segment)> RequestAsync(PluginRuntimeBinding binding, string checker)
        {
            var result = await CallAsync(binding, "getCheckText", $$"""["host-handle","{{checker}}"]""");
            Assert.True(result.IsSuccess);
            return (result.Value!.Property("revision").StringValue, result.Value.Property("segments").Item(0).Property("id").StringValue);
        }

        public static string Issue(string segment, int offset, int length, string kind = "spelling") =>
            $$"""{"segmentId":"{{segment}}","offset":{{offset}},"length":{{length}},"kind":"{{kind}}","message":"A typo","replacements":["the"]}""";
    }

    [Fact]
    public async Task ReadOnlyGrantChecksOnDemandAndTextAndResultsReachOnlyTheNamedComposer()
    {
        var fixture = new Fixture(Grant("compose:read"));
        await using var binding = Fixture.Binding(fixture.Grant);
        Assert.True((await fixture.RegisterAsync(binding, "lt")).IsSuccess);
        var (revision, segment) = await fixture.RequestAsync(binding, "lt");
        var set = await fixture.CallAsync(binding, "setAnnotations", $"[\"host-handle\",\"lt\",\"{revision}\",[{Fixture.Issue(segment, 0, 3)}]]");
        Assert.Equal("1", set.Value!.NumberText);
        var shown = Assert.Single(fixture.Store.Current("host-handle", fixture.Access.Handle.Version, fixture.Registry));
        Assert.Equal("teh", shown.Issue.Text, StringComparer.Ordinal);
        Assert.Equal(PluginErrorCode.Unavailable, (await fixture.CallAsync(binding, "setAnnotations", $"[\"other\",\"lt\",\"{revision}\",[]]")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await fixture.CallAsync(binding, "getCheckText", "[\"other\",\"lt\"]")).Error);
    }

    [Fact]
    public async Task TypingCheckerNeedsComposeCheckForEveryCallEvenWithComposeRead()
    {
        var full = new Fixture(Grant("compose:check", "compose:read"));
        await using var binding = Fixture.Binding(full.Grant);
        Assert.True((await full.RegisterAsync(binding, "lt", "typing")).IsSuccess);
        Assert.True((await full.CallAsync(binding, "getCheckText", "[\"host-handle\",\"lt\"]")).IsSuccess);
        // The grant narrows to compose:read: a typing checker may no longer be read or pushed to.
        var narrowed = full.Create(Grant("compose:read"));
        Assert.Equal(PluginErrorCode.PermissionDenied, (await full.CallAsync(binding, "getCheckText", "[\"host-handle\",\"lt\"]", narrowed)).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await full.CallAsync(binding, "setAnnotations", "[\"host-handle\",\"lt\",\"r\",[]]", narrowed)).Error);
        var write = full.Create(Grant("compose:write"));
        Assert.Equal(PluginErrorCode.PermissionDenied, (await full.CallAsync(binding, "getCheckText", "[\"host-handle\",\"lt\"]", write)).Error);
    }

    [Fact]
    public async Task CheckerOfAnotherBindingOrUnknownIdIsNotReachable()
    {
        var fixture = new Fixture(Grant("compose:read"));
        await using var owner = Fixture.Binding(fixture.Grant);
        await using var other = Fixture.Binding(fixture.Grant);
        Assert.True((await fixture.RegisterAsync(owner, "lt")).IsSuccess);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await fixture.CallAsync(other, "getCheckText", "[\"host-handle\",\"lt\"]")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await fixture.CallAsync(owner, "getCheckText", "[\"host-handle\",\"missing\"]")).Error);
        Assert.Equal(0, fixture.Access.Handle.Begins);
    }

    [Fact]
    public async Task AnnotationsMustBeAListWithinTheCapAndCarryTheIssuedRevision()
    {
        var fixture = new Fixture(Grant("compose:read"));
        await using var binding = Fixture.Binding(fixture.Grant);
        Assert.True((await fixture.RegisterAsync(binding, "lt")).IsSuccess);
        var (revision, segment) = await fixture.RequestAsync(binding, "lt");
        Assert.Equal(PluginErrorCode.InvalidArguments, (await fixture.CallAsync(binding, "setAnnotations", $"[\"host-handle\",\"lt\",\"{revision}\",\"x\"]")).Error);
        var tooMany = string.Join(',', Enumerable.Repeat("null", 601));
        Assert.Equal(PluginErrorCode.InvalidArguments, (await fixture.CallAsync(binding, "setAnnotations", $"[\"host-handle\",\"lt\",\"{revision}\",[{tooMany}]]")).Error);
        var stale = await fixture.CallAsync(binding, "setAnnotations", $"[\"host-handle\",\"lt\",\"not-issued\",[{Fixture.Issue(segment, 0, 3)}]]");
        Assert.Equal("0", stale.Value!.NumberText);
        Assert.Empty(fixture.Store.Current("host-handle", fixture.Access.Handle.Version, fixture.Registry));
    }

    [Fact]
    public async Task ASetReplacesOnlyThatCheckersAnnotationsAndAnEmptyListClearsThem()
    {
        var fixture = new Fixture(Grant("compose:read"));
        await using var binding = Fixture.Binding(fixture.Grant);
        Assert.True((await fixture.RegisterAsync(binding, "a")).IsSuccess);
        Assert.True((await fixture.RegisterAsync(binding, "b")).IsSuccess);
        var (ra, sa) = await fixture.RequestAsync(binding, "a");
        Assert.True((await fixture.CallAsync(binding, "setAnnotations", $"[\"host-handle\",\"a\",\"{ra}\",[{Fixture.Issue(sa, 0, 3)}]]")).IsSuccess);
        var (rb, sb) = await fixture.RequestAsync(binding, "b");
        Assert.True((await fixture.CallAsync(binding, "setAnnotations", $"[\"host-handle\",\"b\",\"{rb}\",[{Fixture.Issue(sb, 4, 3)}]]")).IsSuccess);
        Assert.Equal(2, fixture.Store.Current("host-handle", fixture.Access.Handle.Version, fixture.Registry).Count);
        var (ra2, _) = await fixture.RequestAsync(binding, "a");
        Assert.True((await fixture.CallAsync(binding, "setAnnotations", $"[\"host-handle\",\"a\",\"{ra2}\",[]]")).IsSuccess);
        var left = Assert.Single(fixture.Store.Current("host-handle", fixture.Access.Handle.Version, fixture.Registry));
        Assert.Equal("b", left.Spec.Id, StringComparer.Ordinal);
    }

    [Fact]
    public async Task EditingTheTextOrTurningTheAddOnOffDropsItsAnnotations()
    {
        var fixture = new Fixture(Grant("compose:read"));
        var binding = Fixture.Binding(fixture.Grant);
        Assert.True((await fixture.RegisterAsync(binding, "lt")).IsSuccess);
        var (revision, segment) = await fixture.RequestAsync(binding, "lt");
        Assert.True((await fixture.CallAsync(binding, "setAnnotations", $"[\"host-handle\",\"lt\",\"{revision}\",[{Fixture.Issue(segment, 0, 3)}]]")).IsSuccess);
        Assert.Empty(fixture.Store.Current("host-handle", fixture.Access.Handle.Version + 1, fixture.Registry));
        Assert.Single(fixture.Store.Current("host-handle", fixture.Access.Handle.Version, fixture.Registry));
        await binding.DisposeAsync();
        Assert.Empty(fixture.Store.Current("host-handle", fixture.Access.Handle.Version, fixture.Registry));
    }

    [Fact]
    public async Task CheckCallsShareASixtyAMinuteBudgetAndRefusedCallsDoNotSpendIt()
    {
        var fixture = new Fixture(Grant("compose:read"));
        await using var binding = Fixture.Binding(fixture.Grant);
        Assert.True((await fixture.RegisterAsync(binding, "lt")).IsSuccess);
        for (var i = 0; i < 80; i++) Assert.Equal(PluginErrorCode.InvalidArguments, (await fixture.CallAsync(binding, "getCheckText", "[\"host-handle\",\"nope\"]")).Error);
        for (var i = 0; i < 60; i++) Assert.True((await fixture.CallAsync(binding, "getCheckText", "[\"host-handle\",\"lt\"]")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await fixture.CallAsync(binding, "getCheckText", "[\"host-handle\",\"lt\"]")).Error);
    }

    [Fact]
    public async Task NothingToCheckIsUnavailable()
    {
        var fixture = new Fixture(Grant("compose:read")); fixture.Access.Handle.Text = "   ";
        await using var binding = Fixture.Binding(fixture.Grant);
        Assert.True((await fixture.RegisterAsync(binding, "lt")).IsSuccess);
        Assert.Equal(PluginErrorCode.Unavailable, (await fixture.CallAsync(binding, "getCheckText", "[\"host-handle\",\"lt\"]")).Error);
    }

    // ---- the annotation store on its own ----

    [Fact]
    public void ForgettingAComposerRemovesEveryEntryOfIt()
    {
        var store = new PluginAnnotationStore();
        var registry = new PluginCheckerRegistry(TimeProvider.System);
        Assert.Empty(store.Current("none", 0, registry));
        store.Forget("none");
    }

    private sealed class Access : IPluginComposerAccess
    {
        public Handle Handle { get; } = new();
        public PluginAnnotationStore? Store { set => Handle.Store = value; }
        public IPluginComposerHandle? Resolve(PluginRuntimeBinding binding, string composerId) =>
            string.Equals(composerId, "host-handle", StringComparison.Ordinal) ? Handle : null;
    }

    // What the host handle does with a check, on a plain document.
    private sealed class Handle : IPluginComposerHandle
    {
        private readonly Dictionary<string, PluginCheckRequest> _checks = new(StringComparer.Ordinal);
        public PluginAnnotationStore? Store { get; set; }
        public string Text { get; set; } = "teh quick teh";
        public long Version { get; } = 7;
        public int Begins { get; private set; }
        public string LocalAccountKey => "a";
        public Task<PluginComposerSnapshot> GetSnapshotAsync(CancellationToken ct) => Task.FromResult(new PluginComposerSnapshot("rev", "s", Text, [], [], [], [], false));
        public Task<bool> SetSubjectAsync(string subject, string hostRevision, CancellationToken ct) => throw new NotSupportedException();
        public Task<PluginComposerAttachmentData> ReadAttachmentAsync(string attachmentId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PluginComposerBody> ReadBodyAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<PluginComposerSelection> ReadSelectionAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ReplaceBodyAsync(PluginComposerContent content, string hostRevision, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ReplaceSelectionAsync(PluginComposerContent content, bool below, string hostRevision, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> AttachAsync(PluginComposerBlob blob, string hostRevision, CancellationToken ct) => throw new NotSupportedException();

        public Task<PluginCheckRequest?> BeginCheckAsync(PluginCheckerRegistration spec, CancellationToken ct)
        {
            Begins++;
            var doc = Doc.FromText(Text); doc.HasComposerProvenance = true;
            var request = PluginCheckRequest.Create(doc, "host-handle", "en", Version);
            if (request is not null) _checks[spec.Id] = request;
            return Task.FromResult(request);
        }

        public Task<int> SetAnnotationsAsync(PluginCheckerRegistration spec, string revision, PluginValue annotations, CancellationToken ct)
        {
            if (!_checks.TryGetValue(spec.Id, out var request) || !string.Equals(request.Revision, revision, StringComparison.Ordinal)) return Task.FromResult(0);
            var issues = request.Decode(annotations, spec.Kinds);
            Store!.Set("host-handle", spec, request.DocVersion, issues);
            return Task.FromResult(issues.Count);
        }
    }

    private sealed class Authority(PluginGrantSnapshot grant) : IPluginAuthority
    {
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) => Task.FromResult(Result<PluginGrantSnapshot?>.Ok(grant));
    }

    private sealed class Sessions : IPluginSessionSource
    {
        private readonly IAccountSession _session = new Session();
        public Result<PluginSessionCapture> CaptureActive() => Result<PluginSessionCapture>.Ok(new(_session, 1));
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => Result<PluginSessionCapture>.Ok(new(_session, null));
        public bool IsCurrent(PluginSessionCapture capture) => true;
    }

    private sealed class Session : IAccountSession
    {
        public AccountInfo Account { get; } = new("a", "sample@example.org", "https://jmap.example.org");
        public IJmapClient Client => throw new NotSupportedException();
        public IMailService Mail => throw new NotSupportedException();
        public IMailSync Sync => throw new NotSupportedException();
    }
}
