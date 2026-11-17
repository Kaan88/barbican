// Mirrors: Bulwark lib/plugin-hooks.ts (a plugin's handlers on the app's hook buses; removed when the plugin stops), lib/plugin-runtime.ts (a plugin only
// gets the hooks its manifest asks for); fable-review-2 R2-10 (native handlers start on the thread pool, so the bus budget bounds a blocking one).
// Owner unified (M2 wave 2).

using Barbican.Core.Plugins.Hooks;
using Barbican.Plugins;

namespace Barbican.Core.Plugins.Native;

/// <summary>A plugin's subscriptions to catalogue hooks on the app's <see cref="HookBus"/>.</summary>
internal sealed class PluginHookBridge(NativePluginHost host, LoadedPlugin plugin, HookBus? bus) : IPluginHooks, IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<IDisposable> _subscriptions = [];
    private bool _closed;

    public IDisposable Observe(string hook, Func<HookEvent, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (Refused(hook) is { } why)
        {
            throw new InvalidOperationException(why);
        }

        return bus is null
            ? Track(Nothing.Instance)
            : Track(bus.ObserveAny(hook, (payload, t) =>
            {
                var e = new HookEvent(hook, NativePluginHost.PayloadAsJson(payload));

                // started on the pool: a handler that blocks before its first await cannot hold the caller past the hook's budget
                return new ValueTask(Task.Run(() => handler(e, t), t));
            }));
    }

    public IDisposable Intercept(string hook, Func<HookEvent, CancellationToken, Task<bool>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (Refused(hook) is { } why)
        {
            throw new InvalidOperationException(why);
        }

        return bus is null
            ? Track(Nothing.Instance)
            : Track(bus.InterceptAny(hook, (payload, t) =>
            {
                var e = new HookEvent(hook, NativePluginHost.PayloadAsJson(payload));
                return new ValueTask<bool>(Task.Run(() => handler(e, t), t));
            }));
    }

    public void Dispose()
    {
        List<IDisposable> all;
        lock (_gate)
        {
            _closed = true;
            all = [.. _subscriptions];
            _subscriptions.Clear();
        }

        foreach (var s in all)
        {
            s.Dispose();
        }
    }

    private string? Refused(string hook)
    {
        if (!HookPolicies.IsCatalogued(hook))
        {
            return $"'{hook}' is not a catalogue hook.";
        }

        return plugin.Info.Handles(hook) ? null : $"Plugin '{plugin.Info.Id}' does not list '{hook}' in its manifest.";
    }

    private IDisposable Track(IDisposable subscription)
    {
        lock (_gate)
        {
            if (!_closed)
            {
                _subscriptions.Add(subscription);
                return subscription;
            }
        }

        // the plugin was stopped while it subscribed
        subscription.Dispose();
        host.Report(plugin.Info.Id, "subscribe", new ObjectDisposedException(plugin.Info.Id));
        return subscription;
    }

    private sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();

        public void Dispose()
        {
        }
    }
}
