// Mirrors: M3 plan section 4.5 (IEditorViewModel is the contract; this is what the editor control itself needs beyond it, additive). Rust src/rte/mod.rs on_cmd, ctx_info, source_cmd.

namespace Barbican.Presentation.Editor;

/// <summary>Raised whena command needs the view (dialogs, clipboard, files, the browser).</summary>
public sealed class EditorViewCommandEventArgs(EditorCommand command, string? argument) : EventArgs
{
    public EditorCommand Command { get; } = command;

    public string? Argument { get; } = argument;
}
