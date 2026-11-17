// Mirrors: M2 plan section 3.3 (OverlayLayer is filled at start-up from IEnumerable<IShellOverlay> ordered by ZIndex). Stage F of M2.

using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Shell;

/// <summary>Puts the registered overlays into the shell's overlay layer.</summary>
public static class ShellOverlays
{
    /// <summary>Adds every registered <see cref="IShellOverlay"/> view to <paramref name="layer"/>, lowest ZIndex first.</summary>
    public static void Fill(Grid layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        foreach (var overlay in ViewHost.Services.GetServices<IShellOverlay>().OrderBy(o => o.ZIndex))
        {
            Canvas.SetZIndex(overlay.View, overlay.ZIndex);
            layer.Children.Add(overlay.View);
        }
    }
}
