// Mirrors: M3 plan section 4.6; Rust rte/mod.rs refresh :244, ctx_info :332 (CtxInfo); Bulwark components/email/email-composer.tsx (toolbar state). Contract from the M3 plan; owner editor-view.

using Barbican.Core.Editor;

namespace Barbican.Presentation.Editor;

/// <summary>Creates the view model for a composer's body.</summary>
public interface IEditorViewModelFactory
{
    IEditorViewModel Create(Doc doc, bool plain);
}
