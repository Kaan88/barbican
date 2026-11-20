// Source: Bulwark lib/plugin-sandbox/host-api-checker.ts:24-73 and checkers.ts:53-125.
using System.Text.Json;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Api.Composer;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class CheckerRegistrationTests
{
    private const string Definition = """[{"id":"mock","label":"Test checker","kinds":["grammar"],"debounceMs":1,"warnBeforeSend":true}]""";

    [Theory]
    [InlineData("compose:check", false, true)]
    [InlineData("compose:read", false, false)]
    [InlineData("compose:check", true, false)]
    public async Task TypingRegistrationRequiresItsOwnPermissionAndBackgroundFrame(string permission, bool slot, bool allowed)
    {
        var fixture = Create(permission);
        await using var binding = Binding(fixture.Grant, slot);
        var response = await CallAsync(fixture.Dispatcher, binding, "registerChecker", Definition);
        Assert.Equal(allowed, response.IsSuccess);
        if (!allowed) Assert.Empty(fixture.Registry.Current());
        else
        {
            var checker = Assert.Single(fixture.Registry.Current());
            Assert.Equal(TimeSpan.FromMilliseconds(300), checker.Debounce);
            Assert.True(checker.WarnBeforeSend);
            Assert.Same(binding, checker.Owner);
        }
    }

    [Fact]
    public async Task OnDemandReadPermissionDoesNotExposeTypingTextAndOperationsStayWithinExactOwner()
    {
        var fixture = Create("compose:read");
        await using var owner = Binding(fixture.Grant);
        await using var sibling = Binding(fixture.Grant);
        Assert.True((await CallAsync(fixture.Dispatcher, owner, "registerChecker",
            """[{"id":"mock","label":"Read","kinds":["spelling"],"mode":"on-demand"}]""")).IsSuccess);
        Assert.True(Assert.Single(fixture.Registry.Current()).OnDemand);
        Assert.False((await CallAsync(fixture.Dispatcher, sibling, "unregisterChecker", """["mock"]""")).IsSuccess);
        Assert.False((await CallAsync(fixture.Dispatcher, owner, "updateChecker", """["mock",{"mode":"typing"}]""")).IsSuccess);
        var before = Assert.Single(fixture.Registry.Current());
        Assert.True((await CallAsync(fixture.Dispatcher, owner, "updateChecker", """["mock",{"label":"Updated"}]""")).IsSuccess);
        Assert.True((await CallAsync(fixture.Dispatcher, owner, "recheck", """["mock"]""")).IsSuccess);
        var after = Assert.Single(fixture.Registry.Current());
        Assert.Equal("Updated", after.Label, StringComparer.Ordinal);
        Assert.False(fixture.Registry.Contains(before));
        await owner.DisposeAsync();
        Assert.Empty(fixture.Registry.Current());
    }

    private static (PluginCheckerRegistry Registry, PluginGrantSnapshot Grant, PluginApiDispatcher Dispatcher) Create(string permission)
    {
        var registry = new PluginCheckerRegistry(TimeProvider.System);
        var grant = new PluginGrantSnapshot(new PluginPackageIdentity("probe", "1.0.0", new string('a', 64)), 1, true, [permission], ["a"]);
        return (registry, grant, new PluginApiDispatcher("auto-checker", registry.Methods, new Authority(grant), new Sessions()));
    }

    private static PluginRuntimeBinding Binding(PluginGrantSnapshot grant, bool slot = false) =>
        PluginRuntimeBinding.Create("auto-checker", grant, slot ? PluginFrameKind.Slot : PluginFrameKind.Background, slot ? "composer-header-actions" : null);

    private static async Task<PluginCallOutcome> CallAsync(PluginApiDispatcher dispatcher, PluginRuntimeBinding binding, string name, string args)
    {
        using var json = JsonDocument.Parse(args);
        return await dispatcher.DispatchAsync(binding, new PluginApiRequest("request", "composer." + name, PluginValue.FromJson(json.RootElement)),
            TestContext.Current.CancellationToken);
    }

    private sealed class Authority(PluginGrantSnapshot grant) : IPluginAuthority
    {
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }

        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) =>
            Task.FromResult(Result<PluginGrantSnapshot?>.Ok(grant));
    }

    private sealed class Sessions : IPluginSessionSource
    {
        public Result<PluginSessionCapture> CaptureActive() => throw new InvalidOperationException();
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => throw new InvalidOperationException();
        public bool IsCurrent(PluginSessionCapture capture) => false;
    }
}
