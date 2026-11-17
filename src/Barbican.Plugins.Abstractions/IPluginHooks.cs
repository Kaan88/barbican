// Mirrors: Bulwark lib/plugin-types.ts (api.hooks.on: a plugin subscribes to catalogue hooks by name), lib/plugin-hooks.ts (observer and intercept buses);
// spec/plugins/hooks.csv (the names). M2 plan 6.5: native handlers subscribe on the app's hook bus by catalogue name. Owner unified (M2 wave 2).

namespace Barbican.Plugins;

/// <summary>
/// Subscribes a native plugin to catalogue hooks (spec/plugins/hooks.csv names such as "onAccountRemove"). Only hooks the manifest lists are accepted.
/// Handlers run on the thread pool within the hook's budget; one that throws or is late is abandoned and never stops the app (an intercept of a
/// fail-closed hook then says no).
/// </summary>
public interface IPluginHooks
{
    /// <summary>Observes a hook: told after something happened. Dispose the result to stop.</summary>
    IDisposable Observe(string hook, Func<HookEvent, CancellationToken, Task> handler);

    /// <summary>Intercepts a hook: asked before something happens; false stops it. Dispose the result to stop.</summary>
    IDisposable Intercept(string hook, Func<HookEvent, CancellationToken, Task<bool>> handler);
}
