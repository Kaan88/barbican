// Mirrors: M3 plan section 4.6; Rust rte/mod.rs refresh :244, ctx_info :332 (CtxInfo); Bulwark components/email/email-composer.tsx (toolbar state). Contract from the M3 plan; owner editor-view.

using Barbican.Core.Editor;
using Barbican.Core.Editor.Spell;

namespace Barbican.Presentation.Editor;

/// <summary>Creates an <see cref="EditorViewModel"/> over a session from the core's factory, with the spelling engine and the rewrite seam.</summary>
public sealed class EditorViewModelFactory(IEditorSessionFactory sessions, ISpellEngine spell, IRewriteProvider rewrite) : IEditorViewModelFactory
{
    public IEditorViewModel Create(Doc doc, bool plain)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return new EditorViewModel(sessions.Create(doc, plain), spell, rewrite);
    }
}
