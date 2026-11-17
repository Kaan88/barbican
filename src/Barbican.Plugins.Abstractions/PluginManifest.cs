// Mirrors: the Rust plugin manifest (tests/fixtures/plugins/spam-score/src/lib.rs: api, id, name, version, description, hooks); Bulwark lib/plugin-types.ts (PluginManifest).
// Stub from stage F of M2; owner unified.

namespace Barbican.Plugins;

/// <summary>What a native plugin declares about itself.</summary>
/// <param name="Api">The plugin API version this plugin was built for (1).</param>
/// <param name="Id">The stable id ("spam-score"): the key of the plugin's settings and catalogue entry.</param>
/// <param name="Name">The display name.</param>
/// <param name="Version">The plugin's own version.</param>
/// <param name="Description">One sentence for the plugin list.</param>
/// <param name="Hooks">The hook names it handles ("email.details" for the rows below the subject).</param>
public sealed record PluginManifest(int Api, string Id, string Name, string Version, string Description, IReadOnlyList<string> Hooks);
