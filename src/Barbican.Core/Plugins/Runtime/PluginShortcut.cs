// Mirrors: Bulwark protocol.ts InitDoneMsg.shortcuts.
namespace Barbican.Core.Plugins.Runtime;
public sealed record PluginShortcut(string Id, string Keys, string Label, string? Category = null);
