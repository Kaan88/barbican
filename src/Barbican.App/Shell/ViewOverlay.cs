// Mirrors: M2 plan section 3.3 and 4.2 (IShellOverlay). Owner menus.

namespace Barbican.App.Shell;

/// <summary>An <see cref="IShellOverlay"/> whose view is created when the shell first asks for it (the views take their services from <see cref="ViewHost"/>).</summary>
public sealed class ViewOverlay(Func<UIElement> create, int zIndex) : IShellOverlay
{
    private UIElement? _view;

    /// <inheritdoc />
    public UIElement View => _view ??= create();

    /// <inheritdoc />
    public int ZIndex { get; } = zIndex;
}
