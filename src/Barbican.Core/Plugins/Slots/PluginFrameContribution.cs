// Mirrors: Bulwark frame slot registration; owner is a host object, not an id accepted from RPC.
using Barbican.Core.Plugins.Runtime;
namespace Barbican.Core.Plugins.Slots;
public sealed record PluginFrameContribution(PluginRuntimeBinding Owner, PluginSlotRegistration Registration);
