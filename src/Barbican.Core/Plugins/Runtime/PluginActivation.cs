// Mirrors: Bulwark protocol.ts InitDoneMsg; activation is not proof that registered hooks or slots have executed.
using System.Collections.Immutable;
namespace Barbican.Core.Plugins.Runtime;
public sealed record PluginActivation(ImmutableArray<string> Hooks, ImmutableArray<PluginSlotRegistration> Slots,
    ImmutableArray<PluginShortcut> Shortcuts);
