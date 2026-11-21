// Mirrors: Bulwark runtime.tsx:1584 slot-resize (the sandbox reports its content size; host-bridge.ts hands it to the slot's iframe).
using Barbican.Core.Plugins.Runtime;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>A runtime instance that draws a slot frame: it reports the size of its content to the host that mounted it.</summary>
public interface IPluginSlotFrameInstance : IPluginRuntimeInstance
{
    /// <summary>The content size (height, width when inline) changed; raised on a background thread.</summary>
    event EventHandler<PluginSlotSizeEventArgs>? SlotResized;

    /// <summary>The last reported size, (-1, -1) before the first report.</summary>
    (double Height, double Width) LastSlotSize { get; }
}
