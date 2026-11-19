// Ported acceptance: Bulwark loader.ts and host-bridge.ts startup; M6b hash/grant and revocation-before-code invariants.
using System.Text.Json;
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Automation.Tests.Plugins;

public sealed class PluginSandboxFactoryRedTests
{
    private sealed class Packages(PluginInstallation? current) : IPluginPackageManager
    {
        public int Finds { get; private set; }
        public Func<CancellationToken, Task<Result<Stream>>>? ReadResource { get; set; }
        public Task<Result<PluginInstallation?>> FindAsync(string pluginId, CancellationToken ct) { Finds++; return Task.FromResult(Result<PluginInstallation?>.Ok(current)); }
        public Task<Result<PluginInstallPreview>> InspectAsync(Stream archive, PluginPackageSource source, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<PluginInstallPreview>> ReviewAsync(PluginPackageIdentity installed, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<PluginInstallation>> InstallAsync(PluginInstallDecision decision, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<PluginInstallation>>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<Unit>> SetEnabledAsync(PluginPackageIdentity package, bool enabled, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<Unit>> UninstallAsync(PluginPackageIdentity package, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<Stream>> OpenResourceAsync(PluginPackageIdentity package, string resource, CancellationToken ct) => ReadResource?.Invoke(ct) ?? Task.FromResult(Result<Stream>.Ok(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("export default {}"))));
    }
    private sealed class Authority(PluginGrantSnapshot? current) : IPluginAuthority
    {
        public int Reads { get; private set; }
        public PluginGrantSnapshot? Current { get; set; } = current;
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) { Reads++; return Task.FromResult(Result<PluginGrantSnapshot?>.Ok(Current)); }
    }
    private sealed class Dispatcher : IPluginApiDispatcher
    {
        public Task<PluginCallOutcome> DispatchAsync(PluginRuntimeBinding binding, PluginApiRequest request, CancellationToken ct) => Task.FromResult(PluginCallOutcome.Succeeded());
    }
    private sealed class Views(List<string> steps, bool failNavigation = false) : IPluginSandboxWebViewFactory
    {
        public int Creates { get; private set; }
        public List<string> Profiles { get; } = [];
        public Func<CancellationToken, Task>? BeforeReturn { get; set; }
        public async Task<IPluginSandboxWebView> CreateAsync(string profile, CancellationToken ct) { Creates++; Profiles.Add(profile); steps.Add("view"); if (BeforeReturn is not null) await BeforeReturn(ct); return new View(steps,failNavigation); }
    }
    private sealed class View(List<string> steps, bool failNavigation) : IPluginSandboxWebView
    {
        public object RootIdentity { get; } = new();
        public Uri TrustedRoot { get; } = new("https://plugin.invalid/host/index.html");
        public Task NavigateAsync(Uri uri, CancellationToken ct) { steps.Add("navigate"); return failNavigation ? Task.FromException(new InvalidOperationException("synthetic navigation failure")) : Task.CompletedTask; }
        public Task PostAsync(ReadOnlyMemory<byte> message, CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() { steps.Add("dispose"); return ValueTask.CompletedTask; }
    }
    private sealed class Revocation(List<string> steps) : IPluginRevocationCoordinator
    {
        public Result<IDisposable> Track(PluginRuntimeBinding binding, Func<CancellationToken, ValueTask> teardown) { steps.Add("track"); return Result<IDisposable>.Ok(new Registration()); }
        public Task<Result<Unit>> RevokeAsync(PluginPackageIdentity package, long throughGeneration, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(new Unit()));
        private sealed class Registration : IDisposable { public void Dispose() { } }
    }
    private static (PluginRuntimeStart Start, PluginInstallation Install, PluginGrantSnapshot Grant) Input()
    {
        using var doc = JsonDocument.Parse("{}");
        var package = new PluginPackageIdentity("fixture", "1.0.0", new string('a',64));
        var grant = new PluginGrantSnapshot(package,1,true,["storage:read"],[]);
        var definition = new PluginPackageDefinition(package, PluginRuntimeKind.JavaScript, "index.js", doc.RootElement, ["storage:read"], PluginPackageSource.LocalImport);
        var installation = new PluginInstallation(definition,true,grant,null);
        var state = new PluginHostState("en",doc.RootElement,doc.RootElement,doc.RootElement,doc.RootElement);
        var b = new PluginValueBuilder();
        return (new PluginRuntimeStart(installation,grant,"auto-8931",PluginFrameKind.Background,null,b.Build(b.Object()),state),installation,grant);
    }
    [Red("m6b-runtime")]
    [Fact]
    public async Task Supplied_snapshot_does_not_override_disabled_or_replaced_installed_package()
    {
        var (start, _, grant) = Input(); var packages = new Packages(null); var authority = new Authority(grant); var steps = new List<string>(); var views = new Views(steps);
        var factory = new PluginSandboxFactory(packages,authority,new Revocation(steps),new Dispatcher(),views);
        var result = await factory.StartAsync(start,TestContext.Current.CancellationToken);
        Assert.False(result.IsOk); Assert.Equal(1,packages.Finds); Assert.Equal(0,views.Creates);
    }
    [Red("m6b-runtime")]
    [Fact]
    public async Task Disabled_installed_package_is_not_started()
    {
        var (start, install, grant) = Input(); var steps = new List<string>(); var views = new Views(steps);
        var factory = new PluginSandboxFactory(new Packages(install with { Enabled = false }),new Authority(grant),new Revocation(steps),new Dispatcher(),views);
        Assert.False((await factory.StartAsync(start,TestContext.Current.CancellationToken)).IsOk); Assert.Equal(0,views.Creates);
    }
    [Red("m6b-runtime")]
    [Fact]
    public async Task Changed_installed_hash_is_not_started_from_stale_review()
    {
        var (start, install, grant) = Input(); using var doc = JsonDocument.Parse("{}");
        var changed = new PluginPackageIdentity("fixture","1.0.0",new string('b',64));
        var definition = new PluginPackageDefinition(changed,PluginRuntimeKind.JavaScript,"index.js",doc.RootElement,["storage:read"],PluginPackageSource.LocalImport);
        var steps = new List<string>(); var views = new Views(steps);
        var factory = new PluginSandboxFactory(new Packages(new PluginInstallation(definition,true,grant,null)),new Authority(grant),new Revocation(steps),new Dispatcher(),views);
        Assert.False((await factory.StartAsync(start,TestContext.Current.CancellationToken)).IsOk); Assert.Equal(0,views.Creates);
    }
    [Red("m6b-runtime")]
    [Fact]
    public async Task Native_package_is_not_loaded_by_javascript_runtime_factory()
    {
        var (start, _, grant) = Input(); using var doc = JsonDocument.Parse("{}");
        var definition = new PluginPackageDefinition(grant.Package,PluginRuntimeKind.Native,"plugin.dll",doc.RootElement,["storage:read"],PluginPackageSource.LocalImport);
        var steps = new List<string>(); var views = new Views(steps);
        var factory = new PluginSandboxFactory(new Packages(new PluginInstallation(definition,true,grant,null)),new Authority(grant),new Revocation(steps),new Dispatcher(),views);
        Assert.False((await factory.StartAsync(start,TestContext.Current.CancellationToken)).IsOk); Assert.Equal(0,views.Creates);
    }
    [Red("m6b-runtime")]
    [Fact]
    public async Task Grant_changed_during_resource_read_prevents_untrusted_navigation()
    {
        var (start, install, grant) = Input(); var authority = new Authority(grant); var packages = new Packages(install); var steps = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        packages.ReadResource = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return Result<Stream>.Ok(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("export default {}"))); };
        var factory = new PluginSandboxFactory(packages,authority,new Revocation(steps),new Dispatcher(),new Views(steps));
        var pending = factory.StartAsync(start,TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3),TestContext.Current.CancellationToken);
        authority.Current = new PluginGrantSnapshot(grant.Package,2,true,["storage:read"],[]); release.TrySetResult();
        Assert.False((await pending).IsOk); Assert.DoesNotContain(steps,x => string.Equals(x,"navigate",StringComparison.Ordinal));
    }
    [Red("m6b-runtime")]
    [Fact]
    public async Task Grant_changed_during_view_creation_disposes_view_before_navigation()
    {
        var (start, install, grant) = Input(); var authority = new Authority(grant); var steps = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var views = new Views(steps) { BeforeReturn = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); } };
        var factory = new PluginSandboxFactory(new Packages(install),authority,new Revocation(steps),new Dispatcher(),views);
        var pending = factory.StartAsync(start,TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3),TestContext.Current.CancellationToken);
        authority.Current = new PluginGrantSnapshot(grant.Package,2,true,["storage:read"],[]); release.TrySetResult();
        Assert.False((await pending).IsOk); Assert.DoesNotContain(steps,x => string.Equals(x,"navigate",StringComparison.Ordinal));
        Assert.Contains(steps,x => string.Equals(x,"dispose",StringComparison.Ordinal));
    }
    [Red("m6b-runtime")]
    [Fact]
    public async Task Revoked_grant_generation_cannot_start_from_stale_snapshot()
    {
        var (start, install, grant) = Input(); var packages = new Packages(install); var authority = new Authority(new PluginGrantSnapshot(grant.Package,2,true,["storage:read"],[])); var steps = new List<string>(); var views = new Views(steps);
        var factory = new PluginSandboxFactory(packages,authority,new Revocation(steps),new Dispatcher(),views);
        var result = await factory.StartAsync(start,TestContext.Current.CancellationToken);
        Assert.False(result.IsOk); Assert.Equal(1,authority.Reads); Assert.Equal(0,views.Creates);
    }
    [Red("m6b-runtime")]
    [Fact]
    public async Task Revocation_is_registered_before_any_untrusted_navigation()
    {
        var (start, install, grant) = Input(); var steps = new List<string>();
        var views = new Views(steps, failNavigation:true);
        var factory = new PluginSandboxFactory(new Packages(install),new Authority(grant),new Revocation(steps),new Dispatcher(),views);
        var result = await factory.StartAsync(start,TestContext.Current.CancellationToken);
        Assert.False(result.IsOk); Assert.Contains(steps,x => string.Equals(x,"track",StringComparison.Ordinal)); Assert.Contains(steps,x => string.Equals(x,"navigate",StringComparison.Ordinal));
        Assert.True(steps.IndexOf("track") < steps.IndexOf("navigate"));
        Assert.False(string.Equals(start.Profile, Assert.Single(views.Profiles), StringComparison.Ordinal));
        Assert.False(string.Equals("dev", views.Profiles[0], StringComparison.Ordinal));
        Assert.Contains(steps,x => string.Equals(x,"dispose",StringComparison.Ordinal));
    }
}
