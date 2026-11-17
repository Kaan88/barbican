// Mirrors: PORT-PLAN.md §3.7 (the no-plugin fast path: HasHandlers false, nothing is built). The real bus is the mailcore stream's
// (Plugins/Hooks/HookBus); this one stands in until it is registered, so the sync engine runs without plugins. Owner jmap.

using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Sync;

/// <summary>A hook bus nobody listens on.</summary>
public sealed class NullHookBus : IHookBus
{
    /// <summary>The shared instance.</summary>
    public static NullHookBus Instance { get; } = new();

    /// <inheritdoc/>
    public bool HasHandlers(string hook) => false;

    /// <inheritdoc/>
    public ValueTask NotifyAsync<TPayload>(string hook, TPayload payload, CancellationToken ct) => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public ValueTask<bool> InterceptAsync<TPayload>(string hook, TPayload payload, CancellationToken ct) => ValueTask.FromResult(true);

    /// <inheritdoc/>
    public ValueTask<T> TransformAsync<T>(string hook, T value, CancellationToken ct) => ValueTask.FromResult(value);
}
