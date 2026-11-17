// Mirrors: M2 plan 3.3 (an overlay the shell places in OverlayLayer; the toast host sits below dialogs and menus). Owner actions.

using Barbican.App.Shell;

namespace Barbican.App.Views.Actions;

/// <summary>The toast host as a shell overlay.</summary>
public sealed class ToastOverlay : IShellOverlay
{
    /// <summary>Below the dialog host and the menus (their ZIndex starts at 100).</summary>
    public const int Layer = 50;

    private readonly Lazy<ToastHostView> _view = new(() => new ToastHostView());

    /// <inheritdoc />
    public UIElement View => _view.Value;

    /// <inheritdoc />
    public int ZIndex => Layer;
}
