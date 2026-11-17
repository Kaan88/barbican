// Mirrors: M2 plan section 4.4 (ISnippetSource: search provides, actions renders the marks, I-232); Rust gsearch.rs:365 snippet_segments; RFC 8621 section 5. Contract from stage F of M2.

namespace Barbican.Core.Search;

/// <summary>Search snippets for list rows.</summary>
public interface ISnippetSource
{
    /// <summary>The subject with hit marks, or null when there is none.</summary>
    IReadOnlyList<TextSegment>? SubjectFor(string emailId);

    /// <summary>The preview with hit marks, or null when there is none.</summary>
    IReadOnlyList<TextSegment>? PreviewFor(string emailId);

    /// <summary>Raised when snippets for more messages arrived.</summary>
    event EventHandler<SnippetsArrivedEventArgs>? Arrived;
}
