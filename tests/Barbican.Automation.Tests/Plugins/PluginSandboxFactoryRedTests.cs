// Ported acceptance: Bulwark loader.ts and host-bridge.ts startup; M6b hash/grant and revocation-before-code invariants.
using System.Text.Json;
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Automation.Tests.Plugins;

public sealed class PluginSandboxFactoryRedTests
{
    private sealed class Packages(PluginInstallation? current) : IPluginPackageManager
    {
        public int Finds { get; private set; }
        public string? Resource { get; private set; }
        public Func<CancellationToken, Task<Result<Stream>>>? ReadResource { get; set; }
        public Task<Result<PluginInstallation?>> FindAsync(string pluginId, CancellationToken ct) { Finds++; return Task.FromResult(Result<PluginInstallation?>.Ok(current)); }
        public Task<Result<PluginInstallPreview>> InspectAsync(Stream archive, PluginPackageSource source, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<PluginInstallPreview>> ReviewAsync(PluginPackageIdentity installed, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<PluginInstallation>> InstallAsync(PluginInstallDecision decision, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<PluginInstallation>>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<Unit>> SetEnabledAsync(PluginPackageIdentity package, bool enabled, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<Unit>> UninstallAsync(PluginPackageIdentity package, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<Stream>> OpenResourceAsync(PluginPackageIdentity package, string resource, CancellationToken ct)
        {
            Resource = resource;
            return ReadResource?.Invoke(ct) ?? Task.FromResult(Result<Stream>.Ok(
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes("export default {}"))));
        }
    }
    private sealed class Authority(PluginGrantSnapshot? current) : IPluginDeliveryAuthority
    {
        public int Reads { get; private set; }
        public PluginGrantSnapshot? Current { get; set; } = current;
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) { Reads++; return Task.FromResult(Result<PluginGrantSnapshot?>.Ok(Current)); }
        public Task<bool> TryPostAsync(PluginRuntimeBinding binding, Func<PluginGrantSnapshot, bool> write, CancellationToken ct) =>
            Task.FromResult(!ct.IsCancellationRequested && binding.IsAlive && Current is { Enabled: true } grant &&
                grant.Package == binding.Package && grant.Generation == binding.GrantGeneration && grant.Tier == binding.Tier && write(grant));
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
        public Task<bool> PostAsync(Func<Action<string>, CancellationToken, ValueTask<bool>> deliver, CancellationToken ct) =>
            deliver(_ => { }, ct).AsTask();
        public ValueTask DisposeAsync() { steps.Add("dispose"); return ValueTask.CompletedTask; }
    }
    private sealed class Startup : IPluginSandboxStartup
    {
        public async Task<Result<IPluginRuntimeInstance>> ActivateAsync(PluginSandboxSession session, PluginRuntimeStart trustedRequest, string verifiedCode, CancellationToken ct)
        {
            await session.View.NavigateAsync(session.View.TrustedRoot, ct);
            return Result<IPluginRuntimeInstance>.Fail(new Barbican.Core.Jmap.Errors.ValidationError("Synthetic activation failure."));
        }
    }
    private sealed class SuccessfulStartup(Func<PluginSandboxSession, PluginRuntimeStart, Task>? beforeActivation = null,
        bool failFirstDisposal = false, string[]? hooks = null) : IPluginSandboxStartup
    {
        public ActivatedInstance? Instance { get; private set; }

        public async Task<Result<IPluginRuntimeInstance>> ActivateAsync(PluginSandboxSession session,
            PluginRuntimeStart trustedRequest, string verifiedCode, CancellationToken ct)
        {
            await session.View.NavigateAsync(session.View.TrustedRoot, ct);
            Assert.Equal("export default {}", verifiedCode, StringComparer.Ordinal);
            if (beforeActivation is not null) await beforeActivation(session, trustedRequest);
            Instance = new ActivatedInstance(session.Binding)
            {
                FailFirstDisposal = failFirstDisposal,
                Activation = new([.. hooks ?? []], [], []),
            };
            return Result<IPluginRuntimeInstance>.Ok(Instance);
        }
    }

    private sealed class ActivatedInstance(PluginRuntimeBinding binding) : IPluginRuntimeInstance
    {
        public PluginRuntimeBinding Binding { get; } = binding;
        public PluginActivation Activation { get; init; } = new([], [], []);
        public List<string> InvocationNames { get; } = [];
        public int Disposals { get; private set; }
        public bool FailFirstDisposal { get; init; }

        public Task<Result<PluginValue>> InvokeHookAsync(string hookName, PluginValue arguments, TimeSpan budget, CancellationToken ct)
        {
            InvocationNames.Add(hookName);
            return Task.FromResult(Result<PluginValue>.Ok(arguments));
        }
        public Task<Result<PluginValue>> InvokeCallbackAsync(string callbackId, PluginValue arguments, TimeSpan budget, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<Result<bool>> ShouldShowAsync(string slot, PluginValue context, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<Unit>> SendAsync(PluginValue hostMessage, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask DisposeAsync()
        {
            Disposals++;
            return FailFirstDisposal && Disposals == 1
                ? ValueTask.FromException(new InvalidOperationException("Synthetic teardown error.")) : ValueTask.CompletedTask;
        }
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

    [Fact]
    public async Task Factory_attaches_safe_background_observers_until_its_grant_is_revoked()
    {
        var ct = TestContext.Current.CancellationToken;
        var (start, installation, grant) = Input();
        var startup = new SuccessfulStartup(hooks: [HookNames.OnWindowFocus, "onEmailOpen"]);
        var revocation = new PluginRevocationCoordinator(start.Profile);
        var bus = new HookBus();
        var factory = new PluginSandboxFactory(new Packages(installation), new Authority(grant), revocation,
            new Dispatcher(), new Views([]), startup, bus);

        var result = await factory.StartAsync(start, ct);
        Assert.True(result.IsOk);
        await using var runtime = result.Value!;
        Assert.True(bus.HasHandlers(HookNames.OnWindowFocus));
        Assert.False(bus.HasHandlers("onEmailOpen"));
        await bus.NotifyAsync(HookNames.OnWindowFocus, new EmptyPayload(), ct);
        Assert.Equal(HookNames.OnWindowFocus, Assert.Single(startup.Instance!.InvocationNames));

        Assert.True((await revocation.RevokeAsync(grant.Package, grant.Generation, ct)).IsOk);
        Assert.False(runtime.Binding.IsAlive);
        Assert.False(bus.HasHandlers(HookNames.OnWindowFocus));
    }
    [Fact]
    public async Task Supplied_snapshot_does_not_override_disabled_or_replaced_installed_package()
    {
        var (start, _, grant) = Input(); var packages = new Packages(null); var authority = new Authority(grant); var steps = new List<string>(); var views = new Views(steps);
        var factory = new PluginSandboxFactory(packages,authority,new Revocation(steps),new Dispatcher(),views,new Startup());
        var result = await factory.StartAsync(start,TestContext.Current.CancellationToken);
        Assert.False(result.IsOk); Assert.Equal(1,packages.Finds); Assert.Equal(0,views.Creates);
    }
    [Fact]
    public async Task Disabled_installed_package_is_not_started()
    {
        var (start, install, grant) = Input(); var steps = new List<string>(); var views = new Views(steps);
        var factory = new PluginSandboxFactory(new Packages(install with { Enabled = false }),new Authority(grant),new Revocation(steps),new Dispatcher(),views,new Startup());
        Assert.False((await factory.StartAsync(start,TestContext.Current.CancellationToken)).IsOk); Assert.Equal(0,views.Creates);
    }
    [Fact]
    public async Task Changed_installed_hash_is_not_started_from_stale_review()
    {
        var (start, install, grant) = Input(); using var doc = JsonDocument.Parse("{}");
        var changed = new PluginPackageIdentity("fixture","1.0.0",new string('b',64));
        var definition = new PluginPackageDefinition(changed,PluginRuntimeKind.JavaScript,"index.js",doc.RootElement,["storage:read"],PluginPackageSource.LocalImport);
        var steps = new List<string>(); var views = new Views(steps);
        var factory = new PluginSandboxFactory(new Packages(new PluginInstallation(definition,true,grant,null)),new Authority(grant),new Revocation(steps),new Dispatcher(),views,new Startup());
        Assert.False((await factory.StartAsync(start,TestContext.Current.CancellationToken)).IsOk); Assert.Equal(0,views.Creates);
    }
    [Fact]
    public async Task Native_package_is_not_loaded_by_javascript_runtime_factory()
    {
        var (start, _, grant) = Input(); using var doc = JsonDocument.Parse("{}");
        var definition = new PluginPackageDefinition(grant.Package,PluginRuntimeKind.Native,"plugin.dll",doc.RootElement,["storage:read"],PluginPackageSource.LocalImport);
        var steps = new List<string>(); var views = new Views(steps);
        var factory = new PluginSandboxFactory(new Packages(new PluginInstallation(definition,true,grant,null)),new Authority(grant),new Revocation(steps),new Dispatcher(),views,new Startup());
        Assert.False((await factory.StartAsync(start,TestContext.Current.CancellationToken)).IsOk); Assert.Equal(0,views.Creates);
    }
    [Fact]
    public async Task Grant_changed_during_resource_read_prevents_untrusted_navigation()
    {
        var (start, install, grant) = Input(); var authority = new Authority(grant); var packages = new Packages(install); var steps = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        packages.ReadResource = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return Result<Stream>.Ok(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("export default {}"))); };
        var factory = new PluginSandboxFactory(packages,authority,new Revocation(steps),new Dispatcher(),new Views(steps),new Startup());
        var pending = factory.StartAsync(start,TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3),TestContext.Current.CancellationToken);
        authority.Current = new PluginGrantSnapshot(grant.Package,2,true,["storage:read"],[]); release.TrySetResult();
        Assert.False((await pending).IsOk); Assert.DoesNotContain(steps,x => string.Equals(x,"navigate",StringComparison.Ordinal));
    }
    [Fact]
    public async Task Grant_changed_during_view_creation_disposes_view_before_navigation()
    {
        var (start, install, grant) = Input(); var authority = new Authority(grant); var steps = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var views = new Views(steps) { BeforeReturn = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); } };
        var factory = new PluginSandboxFactory(new Packages(install),authority,new Revocation(steps),new Dispatcher(),views,new Startup());
        var pending = factory.StartAsync(start,TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3),TestContext.Current.CancellationToken);
        authority.Current = new PluginGrantSnapshot(grant.Package,2,true,["storage:read"],[]); release.TrySetResult();
        Assert.False((await pending).IsOk); Assert.DoesNotContain(steps,x => string.Equals(x,"navigate",StringComparison.Ordinal));
        Assert.Contains(steps,x => string.Equals(x,"dispose",StringComparison.Ordinal));
    }
    [Fact]
    public async Task Revoked_grant_generation_cannot_start_from_stale_snapshot()
    {
        var (start, install, grant) = Input(); var packages = new Packages(install); var authority = new Authority(new PluginGrantSnapshot(grant.Package,2,true,["storage:read"],[])); var steps = new List<string>(); var views = new Views(steps);
        var factory = new PluginSandboxFactory(packages,authority,new Revocation(steps),new Dispatcher(),views,new Startup());
        var result = await factory.StartAsync(start,TestContext.Current.CancellationToken);
        Assert.False(result.IsOk); Assert.Equal(1,authority.Reads); Assert.Equal(0,views.Creates);
    }
    [Fact]
    public async Task Revocation_is_registered_before_any_untrusted_navigation()
    {
        var (start, install, grant) = Input(); var steps = new List<string>();
        var views = new Views(steps, failNavigation:true);
        var factory = new PluginSandboxFactory(new Packages(install),new Authority(grant),new Revocation(steps),new Dispatcher(),views,new Startup());
        var result = await factory.StartAsync(start,TestContext.Current.CancellationToken);
        Assert.False(result.IsOk); Assert.Contains(steps,x => string.Equals(x,"track",StringComparison.Ordinal)); Assert.Contains(steps,x => string.Equals(x,"navigate",StringComparison.Ordinal));
        Assert.True(steps.IndexOf("track") < steps.IndexOf("navigate"));
        Assert.False(string.Equals(start.Profile, Assert.Single(views.Profiles), StringComparison.Ordinal));
        Assert.False(string.Equals("dev", views.Profiles[0], StringComparison.Ordinal));
        Assert.Contains(steps,x => string.Equals(x,"dispose",StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthenticatedPackageSuppliesMetadataAndRevocationClosesTheActivatedInstanceOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var (start, install, grant) = Input();
        using var doc = JsonDocument.Parse("""{"name":"Caller-provided data"}""");
        var supplied = new PluginPackageDefinition(grant.Package, PluginRuntimeKind.JavaScript, "untrusted.js",
            doc.RootElement, [], PluginPackageSource.LocalImport);
        var request = new PluginRuntimeStart(new PluginInstallation(supplied, true, grant, null), grant,
            start.Profile, start.Kind, start.Slot, start.Props, start.HostState);
        var packages = new Packages(install);
        var coordinator = new PluginRevocationCoordinator(start.Profile);
        var startup = new SuccessfulStartup((_, trusted) =>
        {
            Assert.Same(install, trusted.Installation);
            return Task.CompletedTask;
        });
        var steps = new List<string>();
        var factory = new PluginSandboxFactory(packages, new Authority(grant), coordinator, new Dispatcher(), new Views(steps), startup);

        var result = await factory.StartAsync(request, ct);
        Assert.True(result.IsOk);
        await using var instance = result.Value!;
        Assert.Equal("index.js", packages.Resource, StringComparer.Ordinal);
        Assert.True((await coordinator.RevokeAsync(grant.Package, grant.Generation, ct)).IsOk);
        Assert.False(instance.Binding.IsAlive);
        Assert.Equal(1, startup.Instance!.Disposals);
        await instance.DisposeAsync();
        Assert.Equal(1, startup.Instance.Disposals);
        Assert.Single(steps, step => string.Equals(step, "dispose", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RevocationDuringActivationRejectsAndClosesALateInstance()
    {
        var ct = TestContext.Current.CancellationToken;
        var (start, install, grant) = Input();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startup = new SuccessfulStartup(async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        });
        var steps = new List<string>();
        var coordinator = new PluginRevocationCoordinator(start.Profile);
        var factory = new PluginSandboxFactory(new Packages(install), new Authority(grant), coordinator,
            new Dispatcher(), new Views(steps), startup);

        var starting = factory.StartAsync(start, ct);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        var revoking = coordinator.RevokeAsync(grant.Package, grant.Generation, ct);
        try { Assert.False(revoking.IsCompleted); }
        finally { release.TrySetResult(); }

        Assert.True((await revoking).IsOk);
        Assert.False((await starting).IsOk);
        Assert.Equal(1, startup.Instance!.Disposals);
        Assert.Single(steps, step => string.Equals(step, "dispose", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RevocationRetriesDisposalWithoutClosingTheViewTwice()
    {
        var ct = TestContext.Current.CancellationToken;
        var (start, install, grant) = Input();
        var steps = new List<string>();
        var startup = new SuccessfulStartup(failFirstDisposal: true);
        var coordinator = new PluginRevocationCoordinator(start.Profile);
        var factory = new PluginSandboxFactory(new Packages(install), new Authority(grant), coordinator,
            new Dispatcher(), new Views(steps), startup);

        var result = await factory.StartAsync(start, ct);
        Assert.True(result.IsOk);
        await using var instance = result.Value!;
        Assert.False((await coordinator.RevokeAsync(grant.Package, grant.Generation, ct)).IsOk);
        Assert.True((await coordinator.RevokeAsync(grant.Package, grant.Generation, ct)).IsOk);
        Assert.Equal(2, startup.Instance!.Disposals);
        Assert.Single(steps, step => string.Equals(step, "dispose", StringComparison.Ordinal));
    }
}
