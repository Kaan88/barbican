// Mirrors: nothing in Bulwark (native add-on host services, API 1 additive; decision D-M6b-08).

namespace Barbican.Plugins;

/// <summary>One option of a <see cref="PluginSettingKind.Choice"/> row.</summary>
/// <param name="Value">The stored text ("1").</param>
/// <param name="Label">What the user reads ("Anthropic").</param>
public sealed record PluginChoice(string Value, string Label);
