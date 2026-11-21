// Source: Bulwark lib/plugin-sandbox/host-api-input.ts:57-70 (each method reads its own argument cap) and host-api-composer.ts:26 (MAX_CONTENT).
// A method's declared budget must admit its own cap and the shared default must keep refusing everything larger.
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Tests.Plugins;

public sealed class PluginArgumentBudgetTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly PluginPackageIdentity Package = new("probe", "1.0.0", new string('a', 64));
    private static PluginGrantSnapshot Grant() => new(Package, 1, true, ["email:write"], ["a"]);

    [Fact]
    public async Task MethodWithoutABudgetRefusesAnArgumentOverTheSharedDefaultAndIsNeverInvoked()
    {
        var probe = new Probe();
        var outcome = await DispatchAsync(probe, TextArgs(300 * 1024));
        Assert.Equal(PluginErrorCode.InvalidArguments, outcome.Error);
        Assert.Null(outcome.Value);
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task DeclaredBudgetAdmitsItsOwnCapAndRefusesTheNextCharacter()
    {
        var probe = new Probe
        {
            Descriptor = new("probe.write", [["email:write"]], TimeSpan.FromSeconds(5))
            {
                ArgumentLimits = new PluginValueLimits(MaxWireBytes: 2_000_000, MaxStringBytes: 700_000),
            },
        };
        Assert.True((await DispatchAsync(probe, TextArgs(340 * 1024))).IsSuccess);
        Assert.Equal(1, probe.Calls);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await DispatchAsync(probe, TextArgs(350 * 1024))).Error);
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public void TheSharedDefaultIsStillTheLibraryBudget()
    {
        Assert.Equal(new PluginValueLimits(), PluginApiDescriptor.DefaultArgumentLimits);
        Assert.Null(new PluginApiDescriptor("probe.write", [["email:write"]], TimeSpan.FromSeconds(5)).ArgumentLimits);
    }

    private static async Task<PluginCallOutcome> DispatchAsync(IPluginApiMethod method, PluginValue arguments)
    {
        var grant = Grant();
        var dispatcher = new PluginApiDispatcher("auto-budget", [method], new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-budget", grant, PluginFrameKind.Background);
        return await dispatcher.DispatchAsync(binding, new PluginApiRequest("request", method.Descriptor.Name, arguments), Ct);
    }

    // The transport already decoded a message of this shape; the builder limit only has to let the test
    // hand the dispatcher the same graph the sandbox would.
    private static PluginValue TextArgs(int chars)
    {
        var builder = new PluginValueBuilder();
        return builder.Build(builder.Array(builder.String(new string('x', chars))),
            new PluginValueLimits(MaxWireBytes: 8 * 1024 * 1024, MaxStringBytes: 4 * 1024 * 1024));
    }

    private sealed class Probe : IPluginApiMethod
    {
        public int Calls { get; private set; }

        public PluginApiDescriptor Descriptor { get; set; } = new("probe.write", [["email:write"]], TimeSpan.FromSeconds(5));

        public PluginErrorCode? ValidateArguments(PluginValue arguments) =>
            arguments.Kind == PluginValueKind.Array ? null : PluginErrorCode.InvalidArguments;

        public Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
        {
            Calls++;
            var builder = new PluginValueBuilder();
            return Task.FromResult(PluginCallOutcome.Succeeded(builder.Build(builder.Boolean(true))));
        }
    }

    private sealed class Authority(PluginGrantSnapshot grant) : IPluginAuthority
    {
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) =>
            Task.FromResult(Result<PluginGrantSnapshot?>.Ok(grant));
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
        public IJmapClient Client => throw new NotSupportedException("No backend accessed in budget tests.");
        public IMailService Mail => throw new NotSupportedException("No backend accessed in budget tests.");
        public IMailSync Sync => throw new NotSupportedException("No backend accessed in budget tests.");
    }
}
