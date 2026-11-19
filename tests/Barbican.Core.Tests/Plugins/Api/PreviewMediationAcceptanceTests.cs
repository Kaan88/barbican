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
    private static PluginApiDispatcher Dispatcher(Preview preview, PluginGrantSnapshot grant) => new("auto-preview", [PluginPreviewMethods.OpenFile(preview)], new Authority(grant), new Sessions());
    [Fact]
    [Red("m6b-mediation")]
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
    [Red("m6b-mediation")]
    public async Task MimeSpoofIsRejectedWithoutCallingHost()
    {
        var preview = new Preview(); var grant = Grant(); var dispatcher = Dispatcher(preview, grant);
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.openFile", RequestArgs([60, 104, 116, 109, 108, 62])), Ct);
        Assert.False(result.IsSuccess); Assert.Equal(0, preview.Calls);
    }
    [Fact]
    [Red("m6b-mediation")]
    public async Task EmptyContentIsRejectedWithoutCallingHost()
    {
        var preview = new Preview(); var grant = Grant(); var dispatcher = Dispatcher(preview, grant);
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.openFile", RequestArgs([])), Ct);
        Assert.False(result.IsSuccess); Assert.Equal(0, preview.Calls);
    }
    [Fact]
    [Red("m6b-mediation")]
    public async Task NoPresenterAcceptanceDoesNotReportSuccess()
    {
        var preview = new Preview { Accept = false }; var grant = Grant(); var dispatcher = Dispatcher(preview, grant);
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.openFile", RequestArgs([37, 80, 68, 70, 45])), Ct);
        Assert.False(result.IsSuccess); Assert.Equal(1, preview.Calls);
    }
    [Fact]
    [Red("m6b-mediation")]
    public async Task OversizeContentIsRejectedWithoutCallingHost()
    {
        var preview = new Preview(); var grant = Grant(); var dispatcher = Dispatcher(preview, grant);
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var bytes = new byte[(25 * 1024 * 1024) + 1]; bytes[0] = 37; bytes[1] = 80; bytes[2] = 68; bytes[3] = 70; bytes[4] = 45;
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.openFile", RequestArgs(bytes)), Ct);
        Assert.False(result.IsSuccess); Assert.Equal(0, preview.Calls);
    }
    [Fact]
    [Red("m6b-mediation")]
    public async Task UnknownFileOptionsAreRejectedWithoutCallingHost()
    {
        var preview = new Preview(); var grant = Grant(); var dispatcher = Dispatcher(preview, grant);
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var b = new PluginValueBuilder(); var args = b.Build(b.Array(b.Object(("content", b.Buffer([37, 80, 68, 70, 45])), ("filename", b.String("file")), ("contentType", b.String("application/pdf")), ("url", b.String("https://example.org")))));
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.openFile", args), Ct);
        Assert.Equal(PluginErrorCode.InvalidArguments, result.Error); Assert.Equal(0, preview.Calls);
    }    [Fact]
    [Red("m6b-mediation")]
    public async Task PrintPolicyDenialNeverCallsPreview()
    {
        var preview = new Preview(); var grant = Grant(); var policy = new PrintPolicy(false);
        var dispatcher = new PluginApiDispatcher("auto-preview", [PluginPreviewMethods.Print(preview, policy)], new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var b = new PluginValueBuilder(); var args = b.Build(b.Array(b.Object(("title", b.String("Report")), ("pdf", b.Buffer([37, 80, 68, 70, 45])))));
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.print", args), Ct);
        Assert.False(result.IsSuccess); Assert.Equal(0, preview.Calls); Assert.Equal(1, policy.Checks);
    }
    [Fact]
    [Red("m6b-mediation")]
    public async Task PdfPrintUsesPrintPurposeAndRequiresRealPreviewAcceptance()
    {
        var preview = new Preview(); var grant = Grant(); var policy = new PrintPolicy(true);
        var dispatcher = new PluginApiDispatcher("auto-preview", [PluginPreviewMethods.Print(preview, policy)], new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-preview", grant, PluginFrameKind.Background);
        var b = new PluginValueBuilder(); var args = b.Build(b.Array(b.Object(("title", b.String("Report")), ("pdf", b.Buffer([37, 80, 68, 70, 45])))));
        var result = await dispatcher.DispatchAsync(binding, new PluginApiRequest("1", "ui.print", args), Ct);
        Assert.True(result.IsSuccess); Assert.Equal(PluginPreviewPurpose.PrintPdf, preview.Purpose); Assert.True(result.Value!.BooleanValue);
    }
    private sealed class PrintPolicy(bool allow) : IPluginPrintPolicy
    {
        public int Checks { get; private set; }
        public Task<bool> CanPrintAsync(CancellationToken ct) { Checks++; return Task.FromResult(allow); }
    }    private sealed class Preview : IPluginFilePreview
    {
        public bool Accept { get; init; } = true;
        public int Calls { get; private set; }
        public PluginPreviewFile? File { get; private set; }
        public PluginPreviewPurpose Purpose { get; private set; }
        public Task<bool> ShowAsync(PluginPreviewFile file, PluginPreviewPurpose purpose, CancellationToken ct) { Calls++; File = file; Purpose = purpose; return Task.FromResult(Accept); }
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
