// Mirrors: M3 plan section 4.2. Owner editor-core.

namespace Barbican.Core.Editor;

/// <summary>Creates an editing session for a composer (one per open composer; the composer loads the document).</summary>
public interface IEditorSessionFactory
{
    IEditorSession Create(Doc doc, bool plain);
}
