// Mirrors: Bulwark protocol.ts init payloads; bundle code is read from the validated package, never supplied as arbitrary host state.
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
namespace Barbican.Core.Plugins.Runtime;
public sealed class PluginRuntimeStart(PluginInstallation installation, PluginGrantSnapshot grant, string profile,
    PluginFrameKind kind, string? slot, PluginValue props, PluginHostState hostState, bool fill = false,
    Barbican.Core.Plugins.Bridge.PluginHostCallbacks? callbacks = null)
{
    public PluginInstallation Installation { get; } = installation;
    public PluginGrantSnapshot Grant { get; } = grant;
    public string Profile { get; } = profile;
    public PluginFrameKind Kind { get; } = kind;
    public string? Slot { get; } = slot;
    public PluginValue Props { get; } = props ?? throw new ArgumentNullException(nameof(props));
    public PluginHostState HostState { get; } = hostState;
    /// <summary>Slot frames only: the add-on's slot offer asked to fill its container (Bulwark protocol.ts InitMsg.fill, runtime.tsx:1522).</summary>
    public bool Fill { get; } = fill;
    /// <summary>Slot frames only: the host functions the frame may call by callback-invoke (Bulwark host-bridge.ts callbackTable); null when the host handed it none.</summary>
    public Barbican.Core.Plugins.Bridge.PluginHostCallbacks? Callbacks { get; } = callbacks;
}
