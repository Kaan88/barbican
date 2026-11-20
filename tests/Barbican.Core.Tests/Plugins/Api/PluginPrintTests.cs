// Bulwark lib/__tests__/plugin-host-print.test.ts and plugin-host-open-file.test.ts.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginPrintTests
{
    private static readonly PluginGrantSnapshot Grant = new(new PluginPackageIdentity("probe", "1.0.0", new string('a', 64)), 1, true, [], []);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PdfTypedArrayUsesSliceAndReturnsTrue()
    {
        PluginPrintRequest? shown = null;
        var method = new PluginPrint(TimeProvider.System, (request, _, budget, _) =>
        { shown = request; return Task.FromResult<PluginErrorCode?>(budget() ? null : PluginErrorCode.RateLimited); });
        await using var binding = PluginRuntimeBinding.Create("auto-print", Grant, PluginFrameKind.Background);
        var builder = new PluginValueBuilder();
        var buffer = builder.Buffer("xx%PDF-1.7yy"u8);
        var args = builder.Build(builder.Array(builder.Object(("title", builder.String("Report")), ("pdf", builder.View(buffer, PluginViewType.Uint8, 2, 8)))));
        var originalPdf = args.Item(0).Property("pdf");
        var result = await method.InvokeAsync(new Context(binding), args, Ct);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.BooleanValue);
        Assert.NotNull(shown);
        Assert.Equal("Report", shown.Title, StringComparer.Ordinal);
        Assert.NotNull(shown.Pdf);
        Assert.True(shown.Pdf.SameNode(originalPdf));
        Assert.Equal("%PDF-1.7"u8.ToArray(), shown.Pdf.BinaryBytes());
        Assert.Null(shown.Html);
    }

    [Fact]
    public async Task HtmlAndMissingTitleReachPresenterAndPolicyRefusalDoesNotSpendBudget()
    {
        var attempts = 0;
        var allowed = false;
        var method = new PluginPrint(TimeProvider.System, (request, _, budget, _) =>
        {
            Assert.Null(request.Title); Assert.Equal("<p>hello</p>", request.Html, StringComparer.Ordinal);
            if (!allowed) return Task.FromResult<PluginErrorCode?>(PluginErrorCode.PermissionDenied);
            attempts++;
            return Task.FromResult<PluginErrorCode?>(budget() ? null : PluginErrorCode.RateLimited);
        });
        await using var binding = PluginRuntimeBinding.Create("auto-print", Grant, PluginFrameKind.Background);
        var context = new Context(binding);
        for (var i = 0; i < 4; i++) Assert.Equal(PluginErrorCode.PermissionDenied, (await method.InvokeAsync(context, Html(), Ct)).Error);
        allowed = true;
        for (var i = 0; i < 3; i++) Assert.True((await method.InvokeAsync(context, Html(), Ct)).IsSuccess);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task RateLimitIsSharedAcrossFramesAndExpires()
    {
        var clock = new FakeTimeProvider();
        var method = new PluginPrint(clock, (_, _, budget, _) => Task.FromResult<PluginErrorCode?>(budget() ? null : PluginErrorCode.RateLimited));
        await using var one = PluginRuntimeBinding.Create("auto-print", Grant, PluginFrameKind.Background);
        await using var two = PluginRuntimeBinding.Create("auto-print", Grant, PluginFrameKind.Background);
        for (var i = 0; i < 3; i++) Assert.True((await method.InvokeAsync(new Context(i % 2 == 0 ? one : two), Html(), Ct)).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await method.InvokeAsync(new Context(two), Html(), Ct)).Error);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await method.InvokeAsync(new Context(two), Html(), Ct)).IsSuccess);
    }

    [Fact]
    public async Task RejectsBadShapeTitleAndPdfWithoutCallingHost()
    {
        var calls = 0;
        var method = new PluginPrint(TimeProvider.System, (_, _, _, _) => { calls++; return Task.FromResult<PluginErrorCode?>(null); });
        await using var binding = PluginRuntimeBinding.Create("auto-print", Grant, PluginFrameKind.Background);
        var context = new Context(binding);
        var builder = new PluginValueBuilder();
        var both = builder.Build(builder.Array(builder.Object(("html", builder.String("x")), ("pdf", builder.Buffer("%PDF-1.7"u8)))));
        Assert.Equal(PluginErrorCode.InvalidArguments, (await method.InvokeAsync(context, both, Ct)).Error);
        builder = new PluginValueBuilder();
        var badTitle = builder.Build(builder.Array(builder.Object(("title", builder.String("bad\nname")), ("html", builder.String("")))));
        Assert.Equal(PluginErrorCode.InvalidArguments, (await method.InvokeAsync(context, badTitle, Ct)).Error);
        builder = new PluginValueBuilder();
        var badPdf = builder.Build(builder.Array(builder.Object(("pdf", builder.Buffer("<html>"u8)))));
        Assert.Equal(PluginErrorCode.InvalidArguments, (await method.InvokeAsync(context, badPdf, Ct)).Error);
        Assert.Equal(0, calls);
    }

    private static PluginValue Html()
    {
        var builder = new PluginValueBuilder();
        return builder.Build(builder.Array(builder.Object(("html", builder.String("<p>hello</p>")))));
    }

    private sealed class Context(PluginRuntimeBinding binding) : IPluginInvocationContext
    {
        public PluginRuntimeBinding Binding { get; } = binding;
        public PluginGrantSnapshot Grant => PluginPrintTests.Grant;
        public bool HasIssuedMutation => false;
        public ValueTask<bool> CheckCurrentAsync(CancellationToken ct) => ValueTask.FromResult(Binding.IsAlive);
        public Result<PluginSessionCapture> CaptureActive() => throw new NotSupportedException();
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => throw new NotSupportedException();
        public Result<Unit> Demand(string permission, string? localAccountKey) => throw new NotSupportedException();
        public void MarkMutationIssued() => throw new NotSupportedException();
    }
}
