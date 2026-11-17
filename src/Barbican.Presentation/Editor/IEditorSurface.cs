// Mirrors: M3 plan section 4.5 (IEditorViewModel is the contract; this is what the editor control itself needs beyond it, additive). Rust src/rte/mod.rs on_cmd, ctx_info, source_cmd.

using Barbican.Core.Editor.Spell;

namespace Barbican.Presentation.Editor;

/// <summary>What the <c>EditorView</c> control binds to; <see cref="EditorViewModel"/> implements it next to <see cref="IEditorViewModel"/>, so a fake view model of the composer still works with the control (the toolbar then follows the session).</summary>
public interface IEditorSurface : IEditorViewModel
{
    /// <summary>Plain-text mode: no formatting, no toolbar (B-RTE-020).</summary>
    bool Plain { get; }

    /// <summary>Show the direction controls (setting <c>rtlEditingSupport</c>).</summary>
    bool RtlSupport { get; set; }

    /// <summary>The HTML the source view shows (the editable part).</summary>
    string SourceText { get; }

    /// <summary>The source was edited: the document follows live, the first edit is one undo step (Rust source_cmd "source-edit").</summary>
    void EditSource(string text);

    /// <summary>Runs a command: the session-only ones here, spelling through the engine, the rest as <see cref="ViewCommandRequested"/>. False when nothing ran.</summary>
    bool Execute(EditorCommand command, string? argument = null);

    event EventHandler<EditorViewCommandEventArgs>? ViewCommandRequested;

    ISpellEngine Spell { get; }

    IRewriteProvider Rewrite { get; }

    /// <summary>The misspelt ranges of a block's text (cached per text by the engine); the word under the caret is left out while it is being typed.</summary>
    IReadOnlyList<SpellRange> Misspelled(int block, string text);

    /// <summary>The misspelt word under <paramref name="block"/>:<paramref name="offset"/>, or null.</summary>
    MisspelledWord? MisspelledAt(int block, int offset);

    /// <summary>The misspelt word under the last right click (the context menu's spelling rows); null clears it.</summary>
    MisspelledWord? ContextWord { get; set; }

    /// <summary>Recomputes <see cref="IEditorViewModel.Toolbar"/> (the view calls it after moving the caret without a session change).</summary>
    void RefreshToolbar();
}
