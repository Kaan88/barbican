// Mirrors: M3 plan section 4.2. Owner editor-core.

namespace Barbican.Core.Editor;

/// <summary>The default factory: a plain <see cref="EditorSession"/>.</summary>
public sealed class EditorSessionFactory : IEditorSessionFactory
{
    public IEditorSession Create(Doc doc, bool plain) => new EditorSession(doc) { Plain = plain };
}
