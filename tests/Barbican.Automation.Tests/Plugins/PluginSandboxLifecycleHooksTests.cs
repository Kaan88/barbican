using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Automation.Tests.Plugins;

public sealed class PluginSandboxLifecycleHooksTests
{
    private static PluginGrantSnapshot Grant() => new(new("fixture", "1.0.0", new string('a', 64)), 1, true, [], []);

    private sealed class Authority(PluginGrantSnapshot grant) : IPluginDeliveryAuthority
    {
        public PluginGrantSnapshot Current { get; set; } = grant;
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) => Task.FromResult(Result<PluginGrantSnapshot?>.Ok(Current));
        public Task<bool> TryPostAsync(PluginRuntimeBinding binding, Func<PluginGrantSnapshot, bool> write, CancellationToken ct) =>
            Task.FromResult(!ct.IsCancellationRequested && binding.IsAlive && Current.Enabled && Current.Package == binding.Package &&
                Current.Generation == binding.GrantGeneration && Current.Tier == binding.Tier && write(Current));
    }

    private sealed class Runtime(PluginRuntimeBinding binding, params string[] hooks) : IPluginRuntimeInstance
    {
        public PluginRuntimeBinding Binding { get; } = binding;
        public PluginActivation Activation { get; } = new([.. hooks], [], []);
        public List<(string Name, PluginValue Arguments)> Calls { get; } = [];
        public Func<CancellationToken, Task<Result<PluginValue>>>? Call { get; set; }
        public Task<Result<PluginValue>> InvokeHookAsync(string hookName, PluginValue arguments, TimeSpan budget, CancellationToken ct)
        {
            Calls.Add((hookName, arguments));
            if (Call is not null) return Call(ct);
            var builder = new PluginValueBuilder();
            return Task.FromResult(Result<PluginValue>.Ok(builder.Build(builder.Undefined())));
        }
        public Task<Result<PluginValue>> InvokeCallbackAsync(string callbackId, PluginValue arguments, TimeSpan budget, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<bool>> ShouldShowAsync(string slot, PluginValue context, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<Unit>> SendAsync(PluginValue hostMessage, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DelayedAuthority(PluginGrantSnapshot grant) : IPluginDeliveryAuthority
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) => Task.FromResult(Result<PluginGrantSnapshot?>.Ok(grant));
        public async Task<bool> TryPostAsync(PluginRuntimeBinding binding, Func<PluginGrantSnapshot, bool> write, CancellationToken ct)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return binding.IsAlive && write(grant);
        }
    }

    [Fact]
    public async Task Implicit_lifecycle_permission_projects_only_fixed_empty_payload_and_deduplicates()
    {
        var grant = Grant(); var binding = PluginRuntimeBinding.Create("auto-8931", grant, PluginFrameKind.Background);
        var runtime = new Runtime(binding, HookNames.OnWindowFocus, HookNames.OnWindowFocus, "onEmailOpen", "unknown");
        var bus = new HookBus();
        using var hooks = await PluginSandboxLifecycleHooks.AttachAsync(bus, runtime, new Authority(grant), TestContext.Current.CancellationToken);
        Assert.NotNull(hooks);
        Assert.False(bus.HasHandlers("onEmailOpen")); Assert.False(bus.HasHandlers("unknown"));
        await bus.NotifyAsync(HookNames.OnWindowFocus, new EmptyPayload(), TestContext.Current.CancellationToken);
        var call = Assert.Single(runtime.Calls);
        Assert.Equal(HookNames.OnWindowFocus, call.Name);
        Assert.Equal(PluginValueKind.Array, call.Arguments.Kind); Assert.Equal(1, call.Arguments.Count);
        Assert.Equal(PluginValueKind.Object, call.Arguments.Item(0).Kind); Assert.Empty(call.Arguments.Item(0).PropertyNames);
        await bus.NotifyAsync(HookNames.OnWindowFocus, "sensitive data", TestContext.Current.CancellationToken);
        Assert.Single(runtime.Calls);
    }

    [Fact]
    public async Task Changed_grant_blocks_delivery_and_binding_disposal_unsubscribes()
    {
        var grant = Grant(); var binding = PluginRuntimeBinding.Create("auto-8931", grant, PluginFrameKind.Background);
        var runtime = new Runtime(binding, HookNames.OnOnline); var authority = new Authority(grant); var bus = new HookBus();
        using var hooks = await PluginSandboxLifecycleHooks.AttachAsync(bus, runtime, authority, TestContext.Current.CancellationToken);
        authority.Current = new(grant.Package, 2, true, [], []);
        await bus.NotifyAsync(HookNames.OnOnline, new EmptyPayload(), TestContext.Current.CancellationToken);
        Assert.Empty(runtime.Calls);
        await binding.DisposeAsync();
        Assert.False(bus.HasHandlers(HookNames.OnOnline));
    }

    [Fact]
    public async Task Dispose_cancels_inflight_call_and_unsubscribes()
    {
        var grant = Grant(); var binding = PluginRuntimeBinding.Create("auto-8931", grant, PluginFrameKind.Background);
        var runtime = new Runtime(binding, HookNames.OnOffline); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Call = async ct => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); };
        var bus = new HookBus(); var hooks = await PluginSandboxLifecycleHooks.AttachAsync(bus, runtime, new Authority(grant), TestContext.Current.CancellationToken);
        Assert.NotNull(hooks);
        var notify = bus.NotifyAsync(HookNames.OnOffline, new EmptyPayload(), TestContext.Current.CancellationToken).AsTask();
        await started.Task; hooks.Dispose(); await notify;
        Assert.False(bus.HasHandlers(HookNames.OnOffline));
    }

    [Fact]
    public async Task Slot_and_stale_grant_cannot_attach()
    {
        var grant = Grant(); var bus = new HookBus(); var authority = new Authority(grant);
        var slot = PluginRuntimeBinding.Create("auto-8931", grant, PluginFrameKind.Slot, "sidebar-widget");
        Assert.Null(await PluginSandboxLifecycleHooks.AttachAsync(bus, new Runtime(slot, HookNames.OnOnline), authority, TestContext.Current.CancellationToken));
        var background = PluginRuntimeBinding.Create("auto-8931", grant, PluginFrameKind.Background);
        authority.Current = new(grant.Package, 2, true, [], []);
        Assert.Null(await PluginSandboxLifecycleHooks.AttachAsync(bus, new Runtime(background, HookNames.OnOnline), authority, TestContext.Current.CancellationToken));
        Assert.False(bus.HasHandlers(HookNames.OnOnline));
        authority.Current = grant;
        Assert.Null(await PluginSandboxLifecycleHooks.AttachAsync(bus, new Runtime(background, "onEmailOpen"), authority, TestContext.Current.CancellationToken));
        var privilegedGrant = new PluginGrantSnapshot(grant.Package, 1, true, [], [], PluginExecutionTier.Privileged);
        var privileged = PluginRuntimeBinding.Create("auto-8931", privilegedGrant, PluginFrameKind.Background);
        Assert.Null(await PluginSandboxLifecycleHooks.AttachAsync(bus, new Runtime(privileged, HookNames.OnOnline), new Authority(privilegedGrant), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Binding_disposal_during_attach_leaves_no_subscription()
    {
        var grant = Grant(); var binding = PluginRuntimeBinding.Create("auto-8931", grant, PluginFrameKind.Background);
        var bus = new HookBus(); var authority = new DelayedAuthority(grant);
        var attaching = PluginSandboxLifecycleHooks.AttachAsync(bus, new Runtime(binding, HookNames.OnOnline), authority, TestContext.Current.CancellationToken);
#pragma warning disable VSTHRD003 // The fake's asynchronous signal is completed by the attach call started above.
        await authority.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
#pragma warning restore VSTHRD003
        await binding.DisposeAsync(); authority.Release.SetResult();
        Assert.Null(await attaching);
        Assert.False(bus.HasHandlers(HookNames.OnOnline));
    }
}
