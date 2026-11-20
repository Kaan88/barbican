// Mirrors: Bulwark lib/plugin-types.ts:1735-1761, plugin-sandbox/loader.ts:335-373, plugin-hooks.ts:862-872.
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>Projects only the host's empty lifecycle notifications to an activated background frame.</summary>
internal sealed class PluginSandboxLifecycleHooks : IDisposable
{
    private static readonly string[] Allowed = [HookNames.OnWindowFocus, HookNames.OnWindowBlur, HookNames.OnOnline, HookNames.OnOffline, HookNames.OnBeforeUnload];
    private readonly Lock _gate = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _stopToken;
    private readonly IPluginRuntimeInstance _runtime;
    private readonly IPluginDeliveryAuthority _authority;
    private CancellationTokenRegistration _bindingRegistration;
    private bool _closed;

    private PluginSandboxLifecycleHooks(IPluginRuntimeInstance runtime, IPluginDeliveryAuthority authority)
    {
        _runtime = runtime;
        _authority = authority;
        _stopToken = _stop.Token;
    }

    internal static async Task<PluginSandboxLifecycleHooks?> AttachAsync(HookBus bus, IPluginRuntimeInstance runtime,
        IPluginDeliveryAuthority authority, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(authority);
        var binding = runtime.Binding;
        if (binding.Kind != PluginFrameKind.Background || binding.Tier != PluginExecutionTier.Untrusted || !binding.IsAlive ||
            !Allowed.Any(hook => runtime.Activation.Hooks.Contains(hook, StringComparer.Ordinal))) return null;
        var attached = new PluginSandboxLifecycleHooks(runtime, authority);
        var registration = binding.Lifetime.Register(attached.Dispose);
        lock (attached._gate)
        {
            if (!attached._closed) attached._bindingRegistration = registration;
            else registration.Unregister();
        }
        try
        {
            var accepted = await authority.TryPostAsync(binding, grant =>
            {
                lock (attached._gate)
                {
                    if (attached._closed || ct.IsCancellationRequested || !Matches(binding, grant)) return false;
                    return binding.TryWriteWhileAlive(() =>
                    {
                        foreach (var hook in Allowed)
                        {
                            if (runtime.Activation.Hooks.Contains(hook, StringComparer.Ordinal))
                                attached._subscriptions.Add(bus.Observe<EmptyPayload>(hook, (_, token) => attached.InvokeAsync(hook, token)));
                        }
                    });
                }
            }, ct).ConfigureAwait(false);
            if (accepted && binding.IsAlive && !attached.IsClosed()) return attached;
        }
        catch
        {
            attached.Dispose();
            throw;
        }
        attached.Dispose();
        return null;
    }

    private bool IsClosed() { lock (_gate) return _closed; }
    private static bool Matches(PluginRuntimeBinding binding, PluginGrantSnapshot grant) => grant.Enabled && grant.Package == binding.Package &&
        grant.Generation == binding.GrantGeneration && grant.Tier == binding.Tier;

    private async ValueTask InvokeAsync(string hook, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stopToken, _runtime.Binding.Lifetime);
        if (IsClosed() || linked.IsCancellationRequested) return;
        try
        {
            var permitted = await _authority.TryPostAsync(_runtime.Binding, grant =>
                !IsClosed() && !linked.IsCancellationRequested && Matches(_runtime.Binding, grant), linked.Token).ConfigureAwait(false);
            if (!permitted || linked.IsCancellationRequested || IsClosed()) return;
            var builder = new PluginValueBuilder();
            var args = builder.Build(builder.Array(builder.Object()));
            _ = await _runtime.InvokeHookAsync(hook, args, HookPolicies.For(hook).Budget, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && linked.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        List<IDisposable> subscriptions;
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            subscriptions = [.. _subscriptions];
            _subscriptions.Clear();
        }
        _bindingRegistration.Unregister();
        _ = StopAsync();
        foreach (var subscription in subscriptions) subscription.Dispose();
    }

    private async Task StopAsync()
    {
        try { await _stop.CancelAsync().ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
}
