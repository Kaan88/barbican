// Mirrors: Bulwark lib/plugin-hook-catalog.ts (observer hooks are fire and forget for the caller). Owner mailcore.

namespace Barbican.Core.Plugins.Hooks;

/// <summary>Call-site helpers: the no-plugin path allocates nothing and the payload is only built when someone listens.</summary>
public static class HookBusExtensions
{
    /// <summary>Tells observers about something that happened, without waiting for them; a no-op when nothing listens.</summary>
    public static void Fire<TPayload>(this IHookBus bus, string hook, Func<TPayload> payload)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(payload);
        if (!bus.HasHandlers(hook))
        {
            return;
        }

        _ = Task.Run(async () => await bus.NotifyAsync(hook, payload(), CancellationToken.None).ConfigureAwait(false));
    }

    /// <summary>Asks interceptors whether an action may go ahead (true when nothing listens).</summary>
    public static ValueTask<bool> AllowAsync<TPayload>(this IHookBus bus, string hook, Func<TPayload> payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(payload);
        return bus.HasHandlers(hook) ? bus.InterceptAsync(hook, payload(), ct) : ValueTask.FromResult(true);
    }

    /// <summary>Runs a transform chain (the value unchanged when nothing listens).</summary>
    public static ValueTask<T> ShapeAsync<T>(this IHookBus bus, string hook, T value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.HasHandlers(hook) ? bus.TransformAsync(hook, value, ct) : ValueTask.FromResult(value);
    }
}
