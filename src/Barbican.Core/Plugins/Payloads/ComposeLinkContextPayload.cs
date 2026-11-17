// Mirrors: Bulwark lib/plugin-hook-catalog.ts:105 (onComposeFromLink: ComposeLinkContext). Owner composer-ui.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The parameters a link carried for one plugin (<c>x-&lt;plugin id&gt;-&lt;name&gt;=&lt;value&gt;</c>), with the prefix gone; only that plugin hears it.</summary>
public sealed record ComposeLinkContextPayload(string PluginId, IReadOnlyDictionary<string, string> Params);
