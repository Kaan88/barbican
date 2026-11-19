// Mirrors: Bulwark lib/plugin-hook-catalog.ts (kinds observer/intercept/transform/takeover), PORT-PLAN.md M6b; M1-plan §4.1.
// Interface owned by mailcore (contract stub from F2). The bus is host-agnostic: no plugin host exists before M6b.

namespace Barbican.Core.Plugins.Hooks;

/// <summary>Where the app fires plugin hooks; with no plugin host registered every call is a no-op.</summary>
public interface IHookBus
{
    /// <summary>The no-plugin fast path: false means nothing listens, so callers skip building the payload.</summary>
    bool HasHandlers(string hook);

    /// <summary>Observer: told after something happened.</summary>
    ValueTask NotifyAsync<TPayload>(string hook, TPayload payload, CancellationToken ct);

    /// <summary>Intercept: asked before something happens; false stops it.</summary>
    ValueTask<bool> InterceptAsync<TPayload>(string hook, TPayload payload, CancellationToken ct);

    /// <summary>Transform: handlers run in order, each sees the previous result.</summary>
    ValueTask<T> TransformAsync<T>(string hook, T value, CancellationToken ct);
    /// <summary>Takeover: explicit default, handled, aborted or failed outcome.</summary>
    ValueTask<HookTakeOverOutcome> TakeOverAsync<TPayload>(string hook, TPayload payload, CancellationToken ct) =>
        throw new Barbican.Core.Common.PortPendingException("hook takeover");
    /// <summary>One ordered pass of vetoes and validated value replacements.</summary>
    ValueTask<HookValueInterceptResult<T>> InterceptWithValueAsync<T>(string hook, T initial, CancellationToken ct) =>
        throw new Barbican.Core.Common.PortPendingException("hook value intercept");
}
