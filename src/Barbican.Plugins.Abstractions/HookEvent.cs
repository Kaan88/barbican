// Mirrors: Bulwark lib/plugin-types.ts (hook payloads are plain JSON objects for a plugin); M2 plan 3.2 (hook payload views). Owner unified (M2 wave 2).

namespace Barbican.Plugins;

/// <summary>One hook call as a plugin sees it: the hook name and its payload as JSON (the plugin cannot see the app's types).</summary>
/// <param name="Hook">The catalogue name ("onAccountRemove").</param>
/// <param name="PayloadJson">The payload serialized as a JSON object ("{}" when there is none).</param>
public sealed record HookEvent(string Hook, string PayloadJson);
