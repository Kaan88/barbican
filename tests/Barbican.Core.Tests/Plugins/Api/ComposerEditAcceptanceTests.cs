// Source: lib/__tests__/plugin-host-composer.test.ts (api.composer on the host: permissions per call, input checks, sanitised content, composer ids,
// stale revisions, 60 edits a minute, attach fields), lib/plugin-sandbox/host-api-composer.ts:34-151.
using System.Text.Json;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
using Barbican.Core.Plugins.Api.Composer;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class ComposerEditAcceptanceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly PluginPackageIdentity Package = new("probe", "1.0.0", new string('a', 64));
    private static PluginGrantSnapshot Grant(params string[] permissions) => new(Package, 1, true, permissions, ["a"]);

    private static async Task<PluginCallOutcome> CallAsync(IPluginApiMethod method, PluginGrantSnapshot grant, Access access, string json, PluginRuntimeBinding? binding = null)
    {
        await using var own = binding is null ? PluginRuntimeBinding.Create("auto-edit", grant, PluginFrameKind.Background) : null;
        var name = method.Descriptor.Name;
        using var doc = JsonDocument.Parse(json);
        return await new PluginApiDispatcher("auto-edit", [method], new Authority(grant), new Sessions())
            .DispatchAsync(binding ?? own!, new PluginApiRequest("request", name, PluginValue.FromJson(doc.RootElement, new PluginValueLimits(MaxWireBytes: 4_000_000, MaxStringBytes: 2_000_000))), Ct);
    }

    private static IPluginComposerEditRateLimiter Limiter() => PluginComposerEditRateLimiter.Create(TimeProvider.System, 10);

    [Fact]
    public async Task GetBodyNeedsComposeReadOnlyAndNeverReachesTheComposerWhenDenied()
    {
        var access = new Access();
        var denied = await CallAsync(PluginComposerEditMethods.GetBody(access), Grant("compose:write"), access, "[\"host-handle\"]");
        Assert.Equal(PluginErrorCode.PermissionDenied, denied.Error);
        Assert.Equal(0, access.Resolutions);
        var allowed = await CallAsync(PluginComposerEditMethods.GetBody(access), Grant("compose:read"), access, "[\"host-handle\"]");
        Assert.True(allowed.IsSuccess);
        Assert.Equal("<p>Hello</p>", allowed.Value!.Property("html").StringValue, StringComparer.Ordinal);
        Assert.Equal("Hello", allowed.Value.Property("text").StringValue, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetSelectionReportsEmptyAndText()
    {
        var access = new Access();
        var empty = await CallAsync(PluginComposerEditMethods.GetSelection(access), Grant("compose:read"), access, "[\"host-handle\"]");
        Assert.True(empty.Value!.Property("empty").BooleanValue);
        access.Handle.Selection = new("<b>x</b>", "x", false);
        var chosen = await CallAsync(PluginComposerEditMethods.GetSelection(access), Grant("compose:read"), access, "[\"host-handle\"]");
        Assert.False(chosen.Value!.Property("empty").BooleanValue);
        Assert.Equal("x", chosen.Value.Property("text").StringValue, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ReplaceNeedsComposeWriteAndTouchesNothingWhenDenied()
    {
        var access = new Access(); var read = Grant("compose:read");
        Assert.Equal(PluginErrorCode.PermissionDenied, (await CallAsync(PluginComposerEditMethods.ReplaceBody(access, Limiter()), read, access, "[\"host-handle\",{\"text\":\"x\"}]")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await CallAsync(PluginComposerEditMethods.ReplaceSelection(access, Limiter()), read, access, "[\"host-handle\",{\"text\":\"x\"}]")).Error);
        Assert.Empty(access.Handle.Calls); Assert.Equal(0, access.Resolutions);
        var write = Grant("compose:write");
        var done = await CallAsync(PluginComposerEditMethods.ReplaceSelection(access, Limiter()), write, access, "[\"host-handle\",{\"text\":\"Dear Bob,\\nthanks\"},{\"below\":true}]");
        Assert.True(done.Value!.BooleanValue);
        Assert.Equal("selection|<p>Dear Bob,</p><p>thanks</p>|Dear Bob,\nthanks|True", Assert.Single(access.Handle.Calls), StringComparer.Ordinal);
    }

    [Fact]
    public async Task PlainTextIsEscapedAndHtmlGoesThroughTheSanitiser()
    {
        var access = new Access(); var write = Grant("compose:write");
        await CallAsync(PluginComposerEditMethods.ReplaceBody(access, Limiter()), write, access, "[\"host-handle\",{\"text\":\"<b>not bold</b>\"}]");
        await CallAsync(PluginComposerEditMethods.ReplaceBody(access, Limiter()), write, access,
            "[\"host-handle\",{\"html\":\"<p onclick=\\\"x()\\\">Hi<script>alert(1)</script></p>\"}]");
        Assert.Equal("body|<p>&lt;b&gt;not bold&lt;/b&gt;</p>|<b>not bold</b>", access.Handle.Calls[0], StringComparer.Ordinal);
        Assert.DoesNotContain("onclick", access.Handle.Calls[1], StringComparison.Ordinal);
        Assert.DoesNotContain("script", access.Handle.Calls[1], StringComparison.Ordinal);
        Assert.Contains("Hi", access.Handle.Calls[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListsQuotesAndHeadingsSurviveInHtmlContent()
    {
        var access = new Access();
        await CallAsync(PluginComposerEditMethods.ReplaceSelection(access, Limiter()), Grant("compose:write"), access,
            "[\"host-handle\",{\"html\":\"<h3>Files</h3><ul><li><a href=\\\"https://cloud.example.org/s/1\\\">a.pdf</a></li></ul><blockquote>q</blockquote>\"}]");
        var call = access.Handle.Calls[0];
        Assert.Contains("<ul><li><a href=\"https://cloud.example.org/s/1\">a.pdf</a></li></ul>", call, StringComparison.Ordinal);
        Assert.Contains("<h3>Files</h3>", call, StringComparison.Ordinal);
        Assert.Contains("<blockquote>q</blockquote>", call, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyAnOpenComposerTheAddOnWasHandedIsReachable()
    {
        var access = new Access();
        var read = Grant("compose:read");
        Assert.Equal(PluginErrorCode.Unavailable, (await CallAsync(PluginComposerEditMethods.GetBody(access), read, access, "[\"guess\"]")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await CallAsync(PluginComposerEditMethods.GetBody(access), read, access, "[]")).Error);
    }

    [Theory]
    [InlineData("[\"host-handle\",{}]")]
    [InlineData("[\"host-handle\",{\"text\":\"a\",\"html\":\"<p>a</p>\"}]")]
    [InlineData("[\"host-handle\",{\"markdown\":\"*a*\"}]")]
    [InlineData("[\"host-handle\",{\"text\":\"a\"},{\"revision\":\"x\"}]")]
    [InlineData("[\"host-handle\",{\"text\":\"a\"},{\"expectedRevision\":\"\"}]")]
    [InlineData("[\"host-handle\",{\"text\":\"a\"},{\"expectedRevision\":1}]")]
    [InlineData("[\"host-handle\",{\"text\":42}]")]
    public async Task ReplaceBodyRefusesOddContentAndOptionsBeforeAnythingHappens(string json)
    {
        var access = new Access();
        Assert.Equal(PluginErrorCode.InvalidArguments, (await CallAsync(PluginComposerEditMethods.ReplaceBody(access, Limiter()), Grant("compose:write"), access, json)).Error);
        Assert.Equal(0, access.Resolutions); Assert.Empty(access.Handle.Calls);
    }

    [Fact]
    public async Task ReplaceSelectionRefusesUnknownOptionsAndContentOverHalfAMebibyte()
    {
        var access = new Access(); var write = Grant("compose:write");
        var method = PluginComposerEditMethods.ReplaceSelection(access, Limiter());
        Assert.Equal(PluginErrorCode.InvalidArguments, (await CallAsync(method, write, access, "[\"host-handle\",{\"text\":\"a\"},{\"at\":\"end\"}]")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await CallAsync(method, write, access, "[\"host-handle\",{\"text\":\"a\"},{\"below\":\"yes\"}]")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await CallAsync(method, write, access, $"[\"host-handle\",{{\"text\":\"{new string('x', (512 * 1024) + 1)}\"}}]")).Error);
        Assert.True((await CallAsync(method, write, access, $"[\"host-handle\",{{\"text\":\"{new string('x', 512 * 1024)}\"}}]")).IsSuccess);
    }

    [Fact]
    public async Task ReplaceBodyCarriesTheWholeHalfMebibyteBodyAndRefusesOneCharacterMore()
    {
        var access = new Access(); var write = Grant("compose:write");
        var method = PluginComposerEditMethods.ReplaceBody(access, Limiter());
        Assert.NotNull(method.Descriptor.ArgumentLimits);
        Assert.True(2L * 512 * 1024 > PluginApiDescriptor.DefaultArgumentLimits.MaxStringBytes);
        Assert.True((await CallAsync(method, write, access, $"[\"host-handle\",{{\"text\":\"{new string('x', 512 * 1024)}\"}}]")).Value!.BooleanValue);
        Assert.Single(access.Handle.Calls);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await CallAsync(method, write, access, $"[\"host-handle\",{{\"text\":\"{new string('x', (512 * 1024) + 1)}\"}}]")).Error);
        Assert.Single(access.Handle.Calls);
    }

    [Fact]
    public async Task StaleExpectedRevisionIsRefusedBeforeTheEditAndTheCurrentOneIsAccepted()
    {
        var access = new Access(); var write = Grant("compose:write");
        var method = PluginComposerEditMethods.ReplaceBody(access, Limiter());
        var stale = await CallAsync(method, write, access, "[\"host-handle\",{\"text\":\"x\"},{\"expectedRevision\":\"older\"}]");
        Assert.Equal(PluginErrorCode.ComposerChanged, stale.Error); Assert.Empty(access.Handle.Calls);
        Assert.True((await CallAsync(method, write, access, "[\"host-handle\",{\"text\":\"x\"},{\"expectedRevision\":\"rev-1\"}]")).Value!.BooleanValue);
        Assert.Equal("rev-1", access.Handle.LastRevision, StringComparer.Ordinal);
    }

    [Fact]
    public async Task AttachNeedsBlobWriteChecksFieldsAndDefaultsTheType()
    {
        var access = new Access();
        var method = PluginComposerEditMethods.Attach(access, Limiter());
        Assert.Equal(PluginErrorCode.PermissionDenied, (await CallAsync(method, Grant("compose:write"), access, "[\"host-handle\",{\"blobId\":\"B1\",\"name\":\"a.pdf\",\"size\":1}]")).Error);
        var attacher = Grant("email:blob-write");
        Assert.True((await CallAsync(method, attacher, access, "[\"host-handle\",{\"blobId\":\"B1\",\"name\":\"a.pdf\",\"size\":10}]")).Value!.BooleanValue);
        Assert.Equal("attach|B1|a.pdf|application/octet-stream|10", Assert.Single(access.Handle.Calls), StringComparer.Ordinal);
        foreach (var bad in new[]
        {
            "{\"blobId\":\"B1\",\"name\":\"a.pdf\",\"size\":-1}", "{\"blobId\":\"B1\",\"name\":\"a.pdf\",\"size\":1.5}", "{\"blobId\":\"B1\",\"name\":\"\",\"size\":1}",
            "{\"blobId\":\"B1\",\"name\":\"a\"}", "{\"name\":\"a\",\"size\":1}", "{\"blobId\":\"B1\",\"name\":\"a\",\"size\":1,\"extra\":1}",
        }) Assert.Equal(PluginErrorCode.InvalidArguments, (await CallAsync(method, attacher, access, $"[\"host-handle\",{bad}]")).Error);
        Assert.Single(access.Handle.Calls);
    }

    [Fact]
    public async Task AttachPassesAnExplicitTypeAndRefusesANameOrTypeOverItsFieldLimit()
    {
        var access = new Access(); var attacher = Grant("email:blob-write");
        var method = PluginComposerEditMethods.Attach(access, Limiter());
        Assert.True((await CallAsync(method, attacher, access, "[\"host-handle\",{\"blobId\":\"B2\",\"name\":\"a.png\",\"type\":\"image/png\",\"size\":7}]")).Value!.BooleanValue);
        Assert.Equal("attach|B2|a.png|image/png|7", Assert.Single(access.Handle.Calls), StringComparer.Ordinal);
        var longName = new string('n', 256); var longType = new string('t', 256);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await CallAsync(method, attacher, access, $"[\"host-handle\",{{\"blobId\":\"B\",\"name\":\"{longName}\",\"size\":1}}]")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await CallAsync(method, attacher, access, $"[\"host-handle\",{{\"blobId\":\"B\",\"name\":\"a\",\"type\":\"{longType}\",\"size\":1}}]")).Error);
        Assert.Single(access.Handle.Calls);
    }

    [Fact]
    public async Task EditsShareOneBudgetOfSixtyAMinuteAcrossMethodsAndRefusedCallsDoNotSpendIt()
    {
        var limiter = PluginComposerEditRateLimiter.Create(new FakeTimeProvider(), 10); var access = new Access();
        var write = Grant("compose:write"); var both = Grant("compose:write", "email:blob-write");
        var body = PluginComposerEditMethods.ReplaceBody(access, limiter); var selection = PluginComposerEditMethods.ReplaceSelection(access, limiter);
        for (var i = 0; i < 100; i++) Assert.Equal(PluginErrorCode.InvalidArguments, (await CallAsync(body, write, access, "[\"host-handle\",{}]")).Error);
        for (var i = 0; i < 30; i++)
        {
            Assert.True((await CallAsync(body, write, access, "[\"host-handle\",{\"text\":\"a\"}]")).IsSuccess);
            Assert.True((await CallAsync(selection, write, access, "[\"host-handle\",{\"text\":\"a\"}]")).IsSuccess);
        }
        Assert.Equal(PluginErrorCode.RateLimited, (await CallAsync(PluginComposerEditMethods.Attach(access, limiter), both, access, "[\"host-handle\",{\"blobId\":\"B\",\"name\":\"n\",\"size\":1}]")).Error);
        Assert.Equal(PluginErrorCode.RateLimited, (await CallAsync(body, write, access, "[\"host-handle\",{\"text\":\"one more\"}]")).Error);
        Assert.Equal(60, access.Handle.Calls.Count);
    }

    [Fact]
    public async Task ABodyReadWhileTheExposedHandleIsReplacedIsDropped()
    {
        var access = new Access(); var original = access.Handle; original.Pause = true;
        var pending = CallAsync(PluginComposerEditMethods.GetBody(access), Grant("compose:read"), access, "[\"host-handle\"]");
        await original.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        access.Handle = new Handle(); original.Release.SetResult();
        var result = await pending;
        Assert.Equal(PluginErrorCode.StaleContext, result.Error); Assert.Null(result.Value);
    }

    [Fact]
    public async Task ClosedComposerIsUnavailable()
    {
        var access = new Access { Open = false };
        Assert.Equal(PluginErrorCode.Unavailable, (await CallAsync(PluginComposerEditMethods.GetBody(access), Grant("compose:read"), access, "[\"host-handle\"]")).Error);
    }

    [Fact]
    public async Task ReplaceWithoutSessionOrStaleAuthorityDoesNotEdit()
    {
        var access = new Access(); var write = Grant("compose:write");
        var dispatcher = new PluginApiDispatcher("auto-edit", [PluginComposerEditMethods.ReplaceBody(access, Limiter())], new Authority(write), new Sessions { Valid = false });
        await using var binding = PluginRuntimeBinding.Create("auto-edit", write, PluginFrameKind.Background);
        using var doc = JsonDocument.Parse("[\"host-handle\",{\"text\":\"x\"}]");
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("request", "composer.replaceBody", PluginValue.FromJson(doc.RootElement)), Ct);
        Assert.False(result.IsSuccess); Assert.Empty(access.Handle.Calls);
    }

    // ---- the sanitiser (email-sanitization.ts sanitizePluginBodyHtml) ----

    [Theory]
    [InlineData("<p onclick=\"x()\">Hi</p>", "<p>Hi</p>")]
    [InlineData("<p>a<script>alert(1)</script>b</p>", "<p>ab</p>")]
    [InlineData("<style>p{color:red}</style><p>a</p>", "<p>a</p>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "<a>x</a>")]
    [InlineData("<a href=\" JaVa\tScript:alert(1)\">x</a>", "<a>x</a>")]
    [InlineData("<img src=\"https://tracker.example.org/p.png\">", "")]
    [InlineData("<img src=\"cid:logo\" alt=\"l\">", "<img src=\"cid:logo\" alt=\"l\">")]
    [InlineData("<img src=\"data:image/png;base64,AAAA\">", "<img src=\"data:image/png;base64,AAAA\">")]
    [InlineData("<img src=\"data:image/svg+xml;base64,AAAA\">", "")]
    [InlineData("<font color=\"red\">x</font>", "x")]
    [InlineData("<p data-x=\"1\" id=\"a\" aria-label=\"l\">x</p>", "<p aria-label=\"l\">x</p>")]
    [InlineData("<p style=\"width:expression(alert(1))\">x</p>", "<p>x</p>")]
    [InlineData("<iframe src=\"https://e.example.org\">inner</iframe><p>a</p>", "<p>a</p>")]
    [InlineData("<svg><script>alert(1)</script></svg><p>a</p>", "<p>a</p>")]
    [InlineData("<!-- c --><p>a</p>", "<p>a</p>")]
    [InlineData("<a href=\"vbscript:x\" onmouseover=\"y()\">x</a>", "<a>x</a>")]
    [InlineData("   ", "")]
    public void SanitiserKeepsOnlyFormattingLinksImagesAndTables(string input, string expected) =>
        Assert.Equal(expected, PluginBodyHtml.Sanitize(input), StringComparer.Ordinal);

    [Fact]
    public void TextBecomesEscapedParagraphsOnePerLine() =>
        Assert.Equal("<p>a &amp; b</p><p>&lt;i&gt;</p><p></p>", PluginBodyHtml.FromText("a & b\r\n<i>\n"), StringComparer.Ordinal);

    // ---- attach gates ----

    [Fact]
    public void AttachGateAppliesUploadAndPerMessageLimits()
    {
        using var raw = JsonDocument.Parse("""{"accounts":{"m":{"accountCapabilities":{"urn:ietf:params:jmap:mail":{"maxSizeAttachmentsPerEmail":100}}}}}""");
        var session = new JmapSession
        {
            Raw = raw.RootElement.Clone(), Limits = new CoreLimits { MaxSizeUpload = 60 },
            PrimaryAccounts = new Dictionary<string, string>(StringComparer.Ordinal) { [JmapCapabilities.Mail] = "m" },
        };
        Assert.True(PluginAttachGate.Allows(session, 40, 1, 60));
        Assert.False(PluginAttachGate.Allows(session, 0, 0, 61));
        Assert.False(PluginAttachGate.Allows(session, 50, 1, 51));
        Assert.False(PluginAttachGate.Allows(session, 0, PluginAttachGate.MaxAttachments, 1));
    }

    private sealed class Access : IPluginComposerAccess
    {
        public Handle Handle { get; set; } = new();
        public bool Open { get; set; } = true;
        public int Resolutions { get; private set; }
        public IPluginComposerHandle? Resolve(PluginRuntimeBinding binding, string composerId)
        {
            Resolutions++;
            return Open && string.Equals(composerId, "host-handle", StringComparison.Ordinal) ? Handle : null;
        }
    }

    private sealed class Handle : IPluginComposerHandle
    {
        public List<string> Calls { get; } = [];
        public string? LastRevision { get; private set; }
        public bool Pause { get; set; }
        public PluginComposerSelection Selection { get; set; } = new(string.Empty, string.Empty, true);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string LocalAccountKey => "a";
        public Task<PluginComposerSnapshot> GetSnapshotAsync(CancellationToken ct) => Task.FromResult(new PluginComposerSnapshot("rev-1", "s", "Hello", [], [], [], [], false));
        public Task<bool> SetSubjectAsync(string subject, string hostRevision, CancellationToken ct) => Task.FromResult(true);
        public Task<PluginComposerAttachmentData> ReadAttachmentAsync(string attachmentId, CancellationToken ct) => throw new NotSupportedException();
        public async Task<PluginComposerBody> ReadBodyAsync(CancellationToken ct)
        {
            Started.TrySetResult();
            if (Pause) await Release.Task.WaitAsync(ct).ConfigureAwait(false);
            return new("<p>Hello</p>", "Hello");
        }
        public Task<PluginComposerSelection> ReadSelectionAsync(CancellationToken ct) => Task.FromResult(Selection);
        public Task<bool> ReplaceBodyAsync(PluginComposerContent content, string hostRevision, CancellationToken ct)
        {
            Calls.Add($"body|{content.Html}|{content.Text}"); LastRevision = hostRevision; return Task.FromResult(true);
        }
        public Task<bool> ReplaceSelectionAsync(PluginComposerContent content, bool below, string hostRevision, CancellationToken ct)
        {
            Calls.Add($"selection|{content.Html}|{content.Text}|{below}"); LastRevision = hostRevision; return Task.FromResult(true);
        }
        public Task<bool> AttachAsync(PluginComposerBlob blob, string hostRevision, CancellationToken ct)
        {
            Calls.Add($"attach|{blob.BlobId}|{blob.Name}|{blob.Type}|{blob.Size}"); return Task.FromResult(true);
        }
        public Task<PluginCheckRequest?> BeginCheckAsync(PluginCheckerRegistration spec, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> SetAnnotationsAsync(PluginCheckerRegistration spec, string revision, PluginValue annotations, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Authority(PluginGrantSnapshot grant) : IPluginAuthority
    {
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) => Task.FromResult(Result<PluginGrantSnapshot?>.Ok(grant));
    }

    private sealed class Sessions : IPluginSessionSource
    {
        private readonly IAccountSession _session = new Session();
        public bool Valid { get; set; } = true;
        public Result<PluginSessionCapture> CaptureActive() => Result<PluginSessionCapture>.Ok(new(_session, 1));
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => Result<PluginSessionCapture>.Ok(new(_session, null));
        public bool IsCurrent(PluginSessionCapture capture) => Valid;
    }

    private sealed class Session : IAccountSession
    {
        public AccountInfo Account { get; } = new("a", "sample@example.org", "https://jmap.example.org");
        public IJmapClient Client => throw new NotSupportedException();
        public IMailService Mail => throw new NotSupportedException();
        public IMailSync Sync => throw new NotSupportedException();
    }
}
