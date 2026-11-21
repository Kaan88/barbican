// Mirrors: Bulwark protocol.ts slot-resize { height, width? } (runtime.tsx:1584).
namespace Barbican.App.Plugins.Sandbox;

/// <summary>The content size a slot frame reported; the width only for an inline slot.</summary>
public sealed class PluginSlotSizeEventArgs(double height, double? width) : EventArgs
{
    public double Height { get; } = height;

    public double? Width { get; } = width;
}
