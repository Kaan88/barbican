// Mirrors: M3 plan section 4.6; Rust rte/mod.rs refresh :244, ctx_info :332 (CtxInfo); Bulwark components/email/email-composer.tsx (toolbar state). Contract stub from the M3 plan; owner editor-view replaces this class.

using Barbican.Core.Common;
using Barbican.Core.Editor;

namespace Barbican.Presentation.Editor;

/// <summary>The default registration: fails with <see cref="PortPendingException"/> until stream editor-view lands.</summary>
public sealed class EditorViewModelFactory : IEditorViewModelFactory
{
    public IEditorViewModel Create(Doc doc, bool plain) => throw new PortPendingException("EditorViewModelFactory.Create");
}
