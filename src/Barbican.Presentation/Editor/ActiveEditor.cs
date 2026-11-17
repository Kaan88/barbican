// Mirrors: M3 plan 6.3 step 3 (the context menu and the command registry act on the editor that has the focus). No Bulwark/Rust counterpart: Rust had one global editor.

namespace Barbican.Presentation.Editor;

/// <summary>The editor that has the keyboard focus (menus and registry commands act on it); set by the control on focus, cleared on focus loss.</summary>
public sealed class ActiveEditor
{
    public IEditorSurface? Current { get; private set; }

    public void Activate(IEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        Current = surface;
    }

    public void Deactivate(IEditorSurface surface)
    {
        if (ReferenceEquals(Current, surface))
        {
            Current = null;
        }
    }
}
