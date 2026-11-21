#if WINDOWS
// The panel the next slot frame's WebView is added to. Flows with the async call chain of one slot start, so concurrent starts never share a mount.
namespace Barbican.App.Plugins.Sandbox;

internal static class PluginSlotMount
{
    private static readonly AsyncLocal<Panel?> Current = new();

    /// <summary>The mount of the slot start running in this call chain.</summary>
    internal static Panel Get() => Current.Value ?? throw new InvalidOperationException("No slot mount is set for this start.");

    /// <summary>Sets the mount for the calls made from here on in this call chain.</summary>
    internal static void Set(Panel mount) => Current.Value = mount;
}
#endif
