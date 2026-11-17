// Mirrors: Rust src/rte/mod.rs state :235 and ctx_info :332; Bulwark components/email/email-composer.tsx (toolbar active states).

using Barbican.Core.Editor;

namespace Barbican.Presentation.Editor;

/// <summary>Computes the <see cref="ToolbarState"/> of a session after every change.</summary>
public static class ToolbarStateBuilder
{
    public static ToolbarState Build(IEditorSession session, bool onMisspelledWord = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        var marks = session.CurrentMarks();
        var focus = session.Doc.Blocks[Math.Clamp(session.Sel.Focus.B, 0, session.Doc.Blocks.Count - 1)];
        return new ToolbarState
        {
            Bold = marks.Bold,
            Italic = marks.Italic,
            Underline = marks.Underline,
            Strike = marks.Strike,
            Code = marks.Code,
            Highlight = marks.Highlight is not null,
            Link = session.CurrentLink(),
            Color = marks.Color,
            Size = marks.Size,
            Block = focus.Kind,
            Align = focus.Align,
            CanUndo = session.CanUndo,
            CanRedo = session.CanRedo,
            InTable = focus.Kind.IsCell,
            OnImage = focus.Kind is BlockKind.Image,
            HasSelection = !session.Sel.IsCollapsed,
            OnMisspelledWord = onMisspelledWord,
        };
    }
}
