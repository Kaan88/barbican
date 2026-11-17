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

        // the payload says what happened now, not what the state is when a pool thread gets to it; and observers hear the events of one bus
        // in the order they happened (one Task.Run each let a later event overtake an earlier one: a drag end before the drop, a column
        // reset before the resize that came first)
        var value = payload();
        var chain = Chains.GetValue(bus, static _ => new Chain());
        lock (chain.Gate)
        {
            chain.Tail = chain.Tail.ContinueWith(
                async _ => await NotifyQuietlyAsync(bus, hook, value).ConfigureAwait(false),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IHookBus, Chain> Chains = new();

#pragma warning disable CA1031 // fire and forget: an observer's failure must not stop the events queued behind it (the bus reports handler errors itself)
    private static async Task NotifyQuietlyAsync<TPayload>(IHookBus bus, string hook, TPayload value)
    {
        try
        {
            await bus.NotifyAsync(hook, value, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // nothing to tell the caller: it did not wait
        }
    }
#pragma warning restore CA1031

    private sealed class Chain
    {
        public Lock Gate { get; } = new();

        public Task Tail { get; set; } = Task.CompletedTask;
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
