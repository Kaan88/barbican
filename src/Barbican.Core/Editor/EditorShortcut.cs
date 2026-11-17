// Mirrors: Bulwark components/email/rich-text-editor.tsx:539-600 (StarterKit + Underline + TextAlign extensions), whose default TipTap keymaps are listed in EditorShortcuts.
// No RFC or Stalwart counterpart. Contract addition of editor-core (additive); editor-view maps the platform key event to a chord string and calls EditorShortcuts.TryExecute.

namespace Barbican.Core.Editor;

/// <summary>One keyboard shortcut of the composer as a command on <see cref="IEditorSession"/>.</summary>
/// <param name="Chord">TipTap notation: modifiers (<c>Mod</c> = Ctrl on Windows and Linux, Cmd on macOS; <c>Alt</c>; <c>Shift</c>) and the key joined by dashes, for example <c>Mod-Shift-z</c>.</param>
/// <param name="Command">The TipTap command name, for tests and the toolbar's tooltips.</param>
/// <param name="Formatting">Ignored in plain-text mode (B-RTE-020): the chord is still consumed so the key does not leak to the control.</param>
/// <param name="Run">Runs the command; false when it had nothing to do (Tab outside a list or table), so the view can let the key move focus.</param>
public sealed record EditorShortcut(string Chord, string Command, bool Formatting, Func<IEditorSession, bool> Run);
