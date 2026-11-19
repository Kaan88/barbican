// Mirrors: Bulwark protocol.ts InitDoneMsg.slots. Slot kind and permissions come from the host catalogue.
namespace Barbican.Core.Plugins.Runtime;
public sealed record PluginSlotRegistration(string Name, bool HasShouldShow, double Order, bool Fill = false, bool StartFolded = false);
