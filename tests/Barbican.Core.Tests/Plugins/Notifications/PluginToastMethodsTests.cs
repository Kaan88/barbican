// Bulwark lib/plugin-sandbox/host-api-toasts.ts and plugin-toast-attribution tests.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Notifications;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Notifications;

public sealed class PluginToastMethodsTests
{
    private static readonly PluginGrantSnapshot Grant = new(new PluginPackageIdentity("probe", "1.0.0", new string('a', 64)), 1, true, [], []);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MethodsSanitizeTextAndKeepSeverityAndHostSourceSeparate()
    {
        var shown = new List<PluginToast>();
        var methods = PluginToastMethods.Create(TimeProvider.System, (toast, _, _) => { shown.Add(toast); return Task.FromResult<PluginErrorCode?>(null); });
        await using var binding = PluginRuntimeBinding.Create("auto-toast", Grant, PluginFrameKind.Background);
        var context = new Context(binding);
        foreach (var method in methods)
            Assert.True((await method.InvokeAsync(context, Arguments("  hello\r\n\u202eworld\u0000 "), Ct)).IsSuccess);
        Assert.Equal([PluginToastKind.Success, PluginToastKind.Error, PluginToastKind.Info, PluginToastKind.Warning], shown.Select(x => x.Kind));
        Assert.All(shown, toast => Assert.Equal("hello world", toast.Message, StringComparer.Ordinal));
        Assert.Equal("Good Name", PluginToastMethods.SourceLabel(" Good\n\u200eName ", "probe"), StringComparer.Ordinal);
        Assert.Equal("probe", PluginToastMethods.SourceLabel(" \u202e ", "probe"), StringComparer.Ordinal);
    }

    [Fact]
    public async Task SharedRateWindowSurvivesFramesAndSilentlyDropsExcess()
    {
        var clock = new FakeTimeProvider();
        var shown = new List<PluginToast>();
        var methods = PluginToastMethods.Create(clock, (toast, _, _) => { shown.Add(toast); return Task.FromResult<PluginErrorCode?>(null); });
        await using var one = PluginRuntimeBinding.Create("auto-toast", Grant, PluginFrameKind.Background);
        await using var two = PluginRuntimeBinding.Create("auto-toast", Grant, PluginFrameKind.Background);
        for (var i = 0; i < 6; i++)
            Assert.True((await methods[i % 4].InvokeAsync(new Context(i % 2 == 0 ? one : two), Arguments("x"), Ct)).IsSuccess);
        Assert.Equal(5, shown.Count);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True((await methods[0].InvokeAsync(new Context(two), Arguments("again"), Ct)).IsSuccess);
        Assert.Equal(6, shown.Count);
    }

    [Fact]
    public async Task StaleInvocationAndPresenterRefusalAreNotReportedAsShown()
    {
        var calls = 0;
        var methods = PluginToastMethods.Create(TimeProvider.System, (_, _, _) => { calls++; return Task.FromResult<PluginErrorCode?>(PluginErrorCode.StaleContext); });
        await using var binding = PluginRuntimeBinding.Create("auto-toast", Grant, PluginFrameKind.Background);
        var context = new Context(binding) { Current = false };
        Assert.Equal(PluginErrorCode.StaleContext, (await methods[0].InvokeAsync(context, Arguments("x"), Ct)).Error);
        Assert.Equal(0, calls);
        context.Current = true;
        Assert.Equal(PluginErrorCode.StaleContext, (await methods[0].InvokeAsync(context, Arguments("x"), Ct)).Error);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task LongInputIsCutAfterNormalization()
    {
        PluginToast? shown = null;
        var methods = PluginToastMethods.Create(TimeProvider.System, (toast, _, _) => { shown = toast; return Task.FromResult<PluginErrorCode?>(null); });
        await using var binding = PluginRuntimeBinding.Create("auto-toast", Grant, PluginFrameKind.Background);
        Assert.True((await methods[0].InvokeAsync(new Context(binding), Arguments(new string('x', 1300)), Ct)).IsSuccess);
        Assert.NotNull(shown);
        Assert.Equal(300, shown.Message.Length);
        Assert.EndsWith("…", shown.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(1.5, "1.5")]
    [InlineData(1e-6, "0.000001")]
    [InlineData(1e-7, "1e-7")]
    [InlineData(1e20, "100000000000000000000")]
    [InlineData(1e21, "1e+21")]
    public async Task NumericPrimitivesUseEcmascriptStyleAndExtraArgumentsAreIgnored(double number, string expected)
    {
        PluginToast? shown = null;
        var methods = PluginToastMethods.Create(TimeProvider.System, (toast, _, _) => { shown = toast; return Task.FromResult<PluginErrorCode?>(null); });
        await using var binding = PluginRuntimeBinding.Create("auto-toast", Grant, PluginFrameKind.Background);
        var builder = new PluginValueBuilder();
        var args = builder.Build(builder.Array(builder.Number(number), builder.Object()));
        Assert.True((await methods[0].InvokeAsync(new Context(binding), args, Ct)).IsSuccess);
        Assert.Equal(expected, shown!.Message, StringComparer.Ordinal);
    }

    [Fact]
    public async Task UnicodeLineBreaksAndMalformedOrCutSurrogatesRemainSafe()
    {
        PluginToast? shown = null;
        var methods = PluginToastMethods.Create(TimeProvider.System, (toast, _, _) => { shown = toast; return Task.FromResult<PluginErrorCode?>(null); });
        await using var binding = PluginRuntimeBinding.Create("auto-toast", Grant, PluginFrameKind.Background);
        var message = "one\u0085two\u2028three\u2029four";
        Assert.True((await methods[0].InvokeAsync(new Context(binding), Arguments(message), Ct)).IsSuccess);
        Assert.Equal("one two three four", shown!.Message, StringComparer.Ordinal);
        Assert.True((await methods[0].InvokeAsync(new Context(binding), Arguments(new string('x', 298) + "😀" + "extra"), Ct)).IsSuccess);
        Assert.Equal(new string('x', 298) + "…", shown!.Message, StringComparer.Ordinal);
    }

    private static PluginValue Arguments(string text)
    {
        var builder = new PluginValueBuilder();
        return builder.Build(builder.Array(builder.String(text)));
    }

    private sealed class Context(PluginRuntimeBinding binding) : IPluginInvocationContext
    {
        public PluginRuntimeBinding Binding { get; } = binding;
        public PluginGrantSnapshot Grant => PluginToastMethodsTests.Grant;
        public bool Current { get; set; } = true;
        public bool HasIssuedMutation => false;
        public ValueTask<bool> CheckCurrentAsync(CancellationToken ct) => ValueTask.FromResult(Current && Binding.IsAlive);
        public Result<PluginSessionCapture> CaptureActive() => throw new NotSupportedException();
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => throw new NotSupportedException();
        public Result<Unit> Demand(string permission, string? localAccountKey) => throw new NotSupportedException();
        public void MarkMutationIssued() => throw new NotSupportedException();
    }
}
