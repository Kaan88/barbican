// Mirrors: Bulwark lib/plugin-hook-catalog.ts (intercept hooks: a handler that returns false stops the action) and lib/plugin-sandbox/host-api.ts:196-217,329,356 (the add-on methods
// report a stopped change as null); Barbican: IHookBus.InterceptAsync through HookBusExtensions.AllowAsync. The text each service passes is the one it showed before.

namespace Barbican.Core.Jmap.Errors;

/// <summary>An add-on's intercept hook stopped the action: nothing was written or sent, and the text is shown to the user.</summary>
/// <param name="Text">What to show the user.</param>
public sealed record HookStoppedError(string Text) : JmapError(Text);
