// Source: lib/__tests__/plugin-host-open-file.test.ts:46-106; host-api-file-viewer.ts:19-119.
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Api.Preview;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PreviewMediationAcceptanceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static PluginGrantSnapshot Grant() => new(new PluginPackageIdentity("probe", "1.0.0", new string('a', 64)), 1, true, [], []);
    private static PluginValue RequestArgs(byte[] bytes, string name = "report", string mime = "application/pdf", bool useView = false)
    {
        var b = new PluginValueBuilder(); var buffer = b.Buffer(bytes);
        var content = useView ? b.View(buffer, PluginViewType.Uint8, 1, bytes.Length - 2) : buffer;
        return b.Build(b.Array(b.Object(("content", content), ("filename", b.String(name)), ("contentType", b.String(mime)))), new PluginValueLimits(MaxWireBytes: 40 * 1024 * 1024, MaxBinaryBytes: 26 * 1024 * 1024));
    }
    private static PluginApiDispatcher Dispatcher(Preview preview, PluginGrantSnapshot grant) => new("auto-preview", [PluginPreviewMethods.OpenFile(preview, PluginPresentationRateLimiter.Create(TimeProvider.System, 10))], new Authority(grant), new Sessions());
    [Fact]
    public async Task PdfViewUsesExactSliceSanitizesNameAndCallsPreview()
    {
        var preview = new Preview(); var grant = Grant(); var dispatcher = Dispatcher(preview, grant);
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var bytes = new byte[] { 99, 37, 80, 68, 70, 45, 49, 88 };
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.openFile", RequestArgs(bytes, "../../etc/passwd", useView: true)), Ct);
        Assert.True(result.IsSuccess); Assert.True(result.Value!.BooleanValue); Assert.Equal(PluginPreviewPurpose.Preview, preview.Purpose);
        Assert.Equal("_.._etc_passwd.pdf", preview.File!.Name, StringComparer.Ordinal);
        Assert.Equal(new byte[] { 37, 80, 68, 70, 45, 49 }, preview.File.Bytes.ToArray());
    }
    [Fact]
    public async Task MimeSpoofIsRejectedWithoutCallingHost()
    {
        var preview = new Preview(); var grant = Grant(); var dispatcher = Dispatcher(preview, grant);
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.openFile", RequestArgs([60, 104, 116, 109, 108, 62])), Ct);
        Assert.False(result.IsSuccess); Assert.Equal(0, preview.Calls);
    }
    [Fact]
    public async Task EmptyContentIsRejectedWithoutCallingHost()
    {
        var preview = new Preview(); var grant = Grant(); var dispatcher = Dispatcher(preview, grant);
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.openFile", RequestArgs([])), Ct);
        Assert.False(result.IsSuccess); Assert.Equal(0, preview.Calls);
    }
    [Fact]
    public async Task NoPresenterAcceptanceDoesNotReportSuccess()
    {
        var preview = new Preview { Accept = false }; var grant = Grant(); var dispatcher = Dispatcher(preview, grant);
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.openFile", RequestArgs([37, 80, 68, 70, 45])), Ct);
        Assert.False(result.IsSuccess); Assert.Equal(1, preview.Calls);
    }
    [Fact]
    public async Task OversizeContentIsRejectedWithoutCallingHost()
    {
        var preview = new Preview(); var grant = Grant(); var dispatcher = Dispatcher(preview, grant);
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var bytes = new byte[(25 * 1024 * 1024) + 1]; bytes[0] = 37; bytes[1] = 80; bytes[2] = 68; bytes[3] = 70; bytes[4] = 45;
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.openFile", RequestArgs(bytes)), Ct);
        Assert.False(result.IsSuccess); Assert.Equal(0, preview.Calls);
    }
    [Fact]
    public async Task UnknownFileOptionsAreRejectedWithoutCallingHost()
    {
        var preview = new Preview(); var grant = Grant(); var dispatcher = Dispatcher(preview, grant);
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var b = new PluginValueBuilder(); var args = b.Build(b.Array(b.Object(("content", b.Buffer([37, 80, 68, 70, 45])), ("filename", b.String("file")), ("contentType", b.String("application/pdf")), ("url", b.String("https://example.org")))));
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.openFile", args), Ct);
        Assert.Equal(PluginErrorCode.InvalidArguments, result.Error); Assert.Equal(0, preview.Calls);
    }
    [Fact]
    public async Task PrintPolicyDenialNeverCallsPreview()
    {
        var preview = new Preview(); var grant = Grant(); var policy = new PrintPolicy(false);
        var dispatcher = new PluginApiDispatcher("auto-preview", [PluginPreviewMethods.Print(preview, policy, PluginPresentationRateLimiter.Create(TimeProvider.System, 10))], new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var b = new PluginValueBuilder(); var args = b.Build(b.Array(b.Object(("title", b.String("Report")), ("pdf", b.Buffer([37, 80, 68, 70, 45])))));
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.print", args), Ct);
        Assert.False(result.IsSuccess); Assert.Equal(0, preview.Calls); Assert.Equal(1, policy.Checks);
    }
    [Fact]
    public async Task PdfPrintUsesPrintPurposeAndRequiresRealPreviewAcceptance()
    {
        var preview = new Preview(); var grant = Grant(); var policy = new PrintPolicy(true);
        var dispatcher = new PluginApiDispatcher("auto-preview", [PluginPreviewMethods.Print(preview, policy, PluginPresentationRateLimiter.Create(TimeProvider.System, 10))], new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var b = new PluginValueBuilder(); var args = b.Build(b.Array(b.Object(("title", b.String("Report")), ("pdf", b.Buffer([37, 80, 68, 70, 45])))));
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.print", args), Ct);
        Assert.True(result.IsSuccess); Assert.Equal(PluginPreviewPurpose.PrintPdf, preview.Purpose); Assert.True(result.Value!.BooleanValue);
    }
    [Fact]
    public async Task SharedRateLimitsAcrossFramesAndKeepsPrintSeparate()
    {
        var preview = new Preview(); var grant = Grant(); var policy = new PrintPolicy(true);
        var limiter = PluginPresentationRateLimiter.Create(TimeProvider.System, 10);
        var dispatcher = new PluginApiDispatcher("auto-preview", [PluginPreviewMethods.OpenFile(preview, limiter), PluginPreviewMethods.Print(preview, policy, limiter)], new Authority(grant), new Sessions());
        await using var first = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        await using var second = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var request = new PluginApiRequest("1", "ui.openFile", RequestArgs([37, 80, 68, 70, 45]));
        for (var i = 0; i < 5; i++) Assert.True((await dispatcher.DispatchAsync(i % 2 == 0 ? first : second, request, Ct)).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await dispatcher.DispatchAsync(second, request, Ct)).Error);
        var b = new PluginValueBuilder(); var args = b.Build(b.Array(b.Object(("pdf", b.Buffer([37, 80, 68, 70, 45])))));
        var print = new PluginApiRequest("2", "ui.print", args);
        for (var i = 0; i < 3; i++) Assert.True((await dispatcher.DispatchAsync(first, print, Ct)).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await dispatcher.DispatchAsync(second, print, Ct)).Error);
        Assert.Equal(8, preview.Calls);
    }
    [Fact]
    public async Task RevokedBindingDuringPrintPolicyCheckNeverOpensPreview()
    {
        var preview = new Preview(); var grant = Grant(); var policy = new PausedPolicy();
        var dispatcher = new PluginApiDispatcher("auto-preview", [PluginPreviewMethods.Print(preview, policy, PluginPresentationRateLimiter.Create(TimeProvider.System, 10))], new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var b = new PluginValueBuilder(); var args = b.Build(b.Array(b.Object(("pdf", b.Buffer([37, 80, 68, 70, 45])))));
        var pending = dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.print", args), Ct);
        await policy.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        await binding.DisposeAsync(); policy.Release.TrySetResult(true);
        var result = await pending; Assert.False(result.IsSuccess); Assert.Equal(0, preview.Calls);
    }
    [Fact]
    public async Task HtmlPrintRemainsUnavailableWithoutIsolatedRenderer()
    {
        var preview = new Preview(); var grant = Grant(); var policy = new PrintPolicy(true);
        var dispatcher = new PluginApiDispatcher("auto-preview", [PluginPreviewMethods.Print(preview, policy, PluginPresentationRateLimiter.Create(TimeProvider.System, 10))], new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var b = new PluginValueBuilder(); var args = b.Build(b.Array(b.Object(("html", b.String("<p>hello</p>")))));
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.print", args), Ct);
        Assert.Equal(PluginErrorCode.Unavailable, result.Error); Assert.Equal(0, preview.Calls);
    }
    [Theory]
    [InlineData("application/pdf", "JVBERi0=")]
    [InlineData("image/png", "iVBORw0KGgo=")]
    [InlineData("image/jpeg", "/9j/")]
    [InlineData("image/gif", "R0lGODlh")]
    [InlineData("image/webp", "UklGRgAAAABXRUJQ")]
    public async Task SourceFileSignaturesAreAccepted(string mime, string base64)
    {
        var preview = new Preview(); var grant = Grant(); var dispatcher = Dispatcher(preview, grant);
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.openFile", RequestArgs(Convert.FromBase64String(base64), mime: mime)), Ct);
        Assert.True(result.IsSuccess); Assert.Equal(mime, preview.File!.ContentType, StringComparer.Ordinal);
    }
    [Fact]
    public async Task BlobAndTypeParametersAreAcceptedWithSanitizedFilename()
    {
        var preview = new Preview(); var grant = Grant(); var dispatcher = Dispatcher(preview, grant);
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var b = new PluginValueBuilder(); var args = b.Build(b.Array(b.Object(("content", b.Blob([37, 80, 68, 70, 45], "application/pdf")), ("filename", b.String(" . . report.html ")), ("contentType", b.String("Application/PDF; charset=binary")))));
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.openFile", args), Ct);
        Assert.True(result.IsSuccess); Assert.Equal("report.html.pdf", preview.File!.Name, StringComparer.Ordinal);
    }
    [Fact]
    public async Task InvalidFileDoesNotConsumePreviewBudget()
    {
        var preview = new Preview(); var grant = Grant(); var dispatcher = Dispatcher(preview, grant);
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        for (var i = 0; i < 6; i++) Assert.False((await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.openFile", RequestArgs([])), Ct)).IsSuccess);
        Assert.True((await dispatcher.DispatchAsync(binding, new PluginApiRequest("2", "ui.openFile", RequestArgs([37, 80, 68, 70, 45])), Ct)).IsSuccess);
        Assert.Equal(1, preview.Calls);
    }    [Fact]
    public async Task QueuedHostRechecksAllAccountPolicyBeforePrintIssue()
    {
        var grant = Grant(); var policy = new MutablePolicy(); var host = new GuardedQueuedPreview(policy);
        var dispatcher = new PluginApiDispatcher("auto-preview", [PluginPreviewMethods.Print(host, policy, PluginPresentationRateLimiter.Create(TimeProvider.System, 10))], new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var b = new PluginValueBuilder(); var args = b.Build(b.Array(b.Object(("pdf", b.Buffer([37, 80, 68, 70, 45])))));
        var pending = dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.print", args), Ct);
        await host.Queued.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        policy.Allowed = false; host.Release.TrySetResult();
        var result = await pending; Assert.False(result.IsSuccess); Assert.Equal(0, host.Issued);
    }
    [Fact]
    public async Task QueuedHostRechecksBindingBeforePreviewIssue()
    {
        var grant = Grant(); var policy = new MutablePolicy(); var host = new GuardedQueuedPreview(policy);
        var dispatcher = new PluginApiDispatcher("auto-preview", [PluginPreviewMethods.OpenFile(host, PluginPresentationRateLimiter.Create(TimeProvider.System, 10))], new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var pending = dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.openFile", RequestArgs([37, 80, 68, 70, 45])), Ct);
        await host.Queued.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        await binding.DisposeAsync(); host.Release.TrySetResult();
        var result = await pending; Assert.False(result.IsSuccess); Assert.Equal(0, host.Issued);
    }
    private sealed class MutablePolicy : IPluginPrintPolicy
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> CanPrintAsync(CancellationToken ct) => Task.FromResult(Allowed);
    }
    private sealed class GuardedQueuedPreview(MutablePolicy policy) : IPluginFilePreview
    {
        public TaskCompletionSource Queued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Issued { get; private set; }
        public async Task<bool> ShowAsync(PluginPreviewFile file, PluginPreviewPurpose purpose, IPluginInvocationContext context, CancellationToken ct)
        {
            Queued.TrySetResult(); await Release.Task.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return false;
            if (purpose == PluginPreviewPurpose.PrintPdf && !await policy.CanPrintAsync(ct).ConfigureAwait(false)) return false;
            Issued++; return true;
        }
    }    [Fact]
    public async Task InvalidPdfStillConsumesSourcePrintBudget()
    {
        var preview = new Preview(); var grant = Grant(); var policy = new PrintPolicy(true);
        var dispatcher = new PluginApiDispatcher("auto-preview", [PluginPreviewMethods.Print(preview, policy, PluginPresentationRateLimiter.Create(TimeProvider.System, 10))], new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var b = new PluginValueBuilder(); var bad = b.Build(b.Array(b.Object(("pdf", b.Buffer([1, 2, 3])))));
        for (var i = 0; i < 3; i++) Assert.Equal(PluginErrorCode.InvalidArguments, (await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.print", bad), Ct)).Error);
        var good = new PluginValueBuilder(); var args = good.Build(good.Array(good.Object(("pdf", good.Buffer([37, 80, 68, 70, 45])))));
        Assert.Equal(PluginErrorCode.RateLimited, (await dispatcher.DispatchAsync(binding, new PluginApiRequest("2", "ui.print", args), Ct)).Error);
        Assert.Equal(0, preview.Calls);
    }    private sealed class PausedPolicy : IPluginPrintPolicy
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<bool> CanPrintAsync(CancellationToken ct) { Started.TrySetResult(); return await Release.Task.WaitAsync(ct).ConfigureAwait(false); }
    }    private sealed class PrintPolicy(bool allow) : IPluginPrintPolicy
    {
        public int Checks { get; private set; }
        public Task<bool> CanPrintAsync(CancellationToken ct) { Checks++; return Task.FromResult(allow); }
    }
    private sealed class Preview : IPluginFilePreview
    {
        public bool Accept { get; init; } = true;
        public int Calls { get; private set; }
        public PluginPreviewFile? File { get; private set; }
        public PluginPreviewPurpose Purpose { get; private set; }
        public Task<bool> ShowAsync(PluginPreviewFile file, PluginPreviewPurpose purpose, IPluginInvocationContext context, CancellationToken ct) { Calls++; File = file; Purpose = purpose; return Task.FromResult(Accept); }
    }
    private sealed class Authority(PluginGrantSnapshot grant) : IPluginAuthority
    {
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) => Task.FromResult(Result<PluginGrantSnapshot?>.Ok(grant));
    }
    private sealed class Sessions : IPluginSessionSource
    {
        public Result<PluginSessionCapture> CaptureActive() => Result<PluginSessionCapture>.Fail(new ValidationError("No account."));
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => Result<PluginSessionCapture>.Fail(new ValidationError("No account."));
        public bool IsCurrent(PluginSessionCapture capture) => false;
    }
}
