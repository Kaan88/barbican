// Mirrors: M3 plan section 4.5 ("composer-ui creates the view model, puts the EditorView in its layout through a ContentPresenter"). The one seam between the composer pane and the editor control of stream editor-view:
// the editor registration sets <see cref="EditorFactory"/> to draw an editor view model; until it does (the editor has not landed in this build) the composer shows a plain text stand-in. Owner composer-ui.

using Barbican.Presentation.Editor;

namespace Barbican.App.Views.Compose;

/// <summary>Where the editor control plugs into the composer.</summary>
public static class ComposerBodyHost
{
    /// <summary>Creates the editor control for an editor view model; set by the editor view registration (null: the stand-in text box).</summary>
    public static Func<IEditorViewModel, UIElement>? EditorFactory { get; set; }
}
