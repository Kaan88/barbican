// Source: Bulwark lib/plugin-sandbox/host-api-file-viewer.ts and plugin-host-open-file.test.ts.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginOpenFileTests
{
    private static readonly PluginGrantSnapshot Grant = new(new PluginPackageIdentity("probe", "1.0.0", new string('a', 64)), 1, true, [], []);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TypedArraySlicePreservesOnlyVisibleBytesAndSanitizesLabel()
    {
        PluginPreviewFile? shown = null;
        var method = new PluginOpenFile(TimeProvider.System, (file, _, _) => { shown = file; return Task.FromResult<PluginErrorCode?>(null); });
        await using var binding = PluginRuntimeBinding.Create("auto-preview", Grant, PluginFrameKind.Background);
        var builder = new PluginValueBuilder();
        var buffer = builder.Buffer("xx%PDF-1.7yy"u8);
        var content = builder.View(buffer, PluginViewType.Uint8, 2, 8);
        var result = await method.InvokeAsync(new Context(binding), Arguments(builder, content, " ../A\u202ekte:01 ", "application/pdf"), Ct);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.BooleanValue);
        Assert.NotNull(shown);
        Assert.Equal("%PDF-1.7"u8.ToArray(), shown.Bytes);
        Assert.Equal("_Akte_01.pdf", shown.Name, StringComparer.Ordinal);
        Assert.Equal("application/pdf", shown.ContentType, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("text/html", "<html>bad</html>")]
    [InlineData("application/pdf", "<html>bad</html>")]
    [InlineData("image/png", "%PDF-1.7")]
    public async Task RejectsUnsupportedOrMismatchedContent(string mime, string bytes)
    {
        var calls = 0;
        var method = new PluginOpenFile(TimeProvider.System, (_, _, _) => { calls++; return Task.FromResult<PluginErrorCode?>(null); });
        await using var binding = PluginRuntimeBinding.Create("auto-preview", Grant, PluginFrameKind.Background);
        var builder = new PluginValueBuilder();
        var args = Arguments(builder, builder.Buffer(System.Text.Encoding.UTF8.GetBytes(bytes)), "x", mime);
        var result = await method.InvokeAsync(new Context(binding), args, Ct);
        Assert.Equal(PluginErrorCode.InvalidArguments, result.Error);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task RevocationPreventsPresentation()
    {
        var calls = 0;
        var method = new PluginOpenFile(TimeProvider.System, (_, _, _) => { calls++; return Task.FromResult<PluginErrorCode?>(null); });
        await using var binding = PluginRuntimeBinding.Create("auto-preview", Grant, PluginFrameKind.Background);
        var context = new Context(binding) { Current = false };
        var result = await method.InvokeAsync(context, Pdf(), Ct);
        Assert.Equal(PluginErrorCode.StaleContext, result.Error);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task FivePerMinuteIsSharedAcrossFramesOfSamePackage()
    {
        var calls = 0;
        var method = new PluginOpenFile(TimeProvider.System, (_, _, _) => { calls++; return Task.FromResult<PluginErrorCode?>(null); });
        await using var first = PluginRuntimeBinding.Create("auto-preview", Grant, PluginFrameKind.Background);
        await using var second = PluginRuntimeBinding.Create("auto-preview", Grant, PluginFrameKind.Background);
        for (var i = 0; i < 5; i++)
            Assert.True((await method.InvokeAsync(new Context(i % 2 == 0 ? first : second), Pdf(), Ct)).IsSuccess);
        var denied = await method.InvokeAsync(new Context(second), Pdf(), Ct);
        Assert.Equal(PluginErrorCode.RateLimited, denied.Error);
        Assert.Equal(5, calls);
    }

    private static PluginValue Pdf()
    {
        var builder = new PluginValueBuilder();
        return Arguments(builder, builder.Buffer("%PDF-1.7"u8), "file", "application/pdf");
    }

    private static PluginValue Arguments(PluginValueBuilder builder, int content, string filename, string mime) =>
        builder.Build(builder.Array(builder.Object(("content", content), ("filename", builder.String(filename)), ("contentType", builder.String(mime)))));

    private sealed class Context(PluginRuntimeBinding binding) : IPluginInvocationContext
    {
        public PluginRuntimeBinding Binding { get; } = binding;
        public PluginGrantSnapshot Grant => PluginOpenFileTests.Grant;
        public bool Current { get; set; } = true;
        public bool HasIssuedMutation => false;
        public ValueTask<bool> CheckCurrentAsync(CancellationToken ct) => ValueTask.FromResult(Current && Binding.IsAlive);
        public Result<PluginSessionCapture> CaptureActive() => throw new NotSupportedException();
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => throw new NotSupportedException();
        public Result<Unit> Demand(string permission, string? localAccountKey) => throw new NotSupportedException();
        public void MarkMutationIssued() => throw new NotSupportedException();
    }
}
