// Mirrors: M3 plan section 4.6; Rust rte/mod.rs refresh :244, ctx_info :332 (CtxInfo); Bulwark components/email/email-composer.tsx (toolbar state). Contract from the M3 plan; owner editor-view implements, composer-ui consumes.

using Barbican.Core.Editor;

namespace Barbican.Presentation.Editor;

/// <summary>
/// The view model of one editor instance, between the editing session and the editor control. The composer shell creates it from <see cref="IEditorViewModelFactory"/>, puts it in its layout, and reads
/// <see cref="Session"/> for the document (send, drafts, autosave keys, inline images). The shell never reaches into the control.
/// </summary>
public interface IEditorViewModel
{
    IEditorSession Session { get; }

    ToolbarState Toolbar { get; }

    /// <summary>The HTML source view is open (it replaces the canvas; the quoted original stays out of it).</summary>
    bool SourceOpen { get; }

    /// <summary>Opens or closes the source view; closing applies the edited source (<c>DocHtml.ApplySource</c>).</summary>
    void ToggleSource();

    /// <summary>Asks the control to take keyboard focus (opening the composer, after a toolbar command).</summary>
    void RequestFocus();

    /// <summary>Set by the composer: the @ popup's data (null turns @ mentions off).</summary>
    IMentionSource? Mentions { get; set; }

    event EventHandler? FocusRequested;

    event EventHandler? ToolbarChanged;
}
