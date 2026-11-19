// Mirrors: Bulwark ui.setActions owner attribution and onAction dispatch.
using Barbican.Core.Plugins.Runtime;
namespace Barbican.Core.Plugins.Slots;
public sealed record PluginActionContribution(PluginRuntimeBinding Owner, string Slot, PluginAction Action);
