// Mirrors: Rust compose.rs (rte::export_html / rte::to_text / rte::html::import*, clean_quoted_html, html_text); M3 plan section 5 ("What A needs from B": the Func<Doc, string> seam until editor-core's DocHtml lands). Owner send-pipeline.

using Barbican.Core.Editor;

namespace Barbican.Core.Compose;

/// <summary>The conversions between the composer document and HTML or text that the send pipeline needs: one seam over <see cref="DocHtml"/> so the pipeline is tested before editor-core's port lands and with exact strings.</summary>
public interface IDocCodec
{
    /// <summary>The document as HTML (Bulwark's inline styles and wrapper).</summary>
    string Export(Doc doc);

    /// <summary>The document as plain text (quoted blocks as "&gt; " lines).</summary>
    string ToText(Doc doc);

    /// <summary>Blocks for an HTML fragment (a signature, a template body).</summary>
    IReadOnlyList<Block> Import(string html);

    /// <summary>Blocks for plain text.</summary>
    IReadOnlyList<Block> ImportText(string text);

    /// <summary>Blocks for a saved draft's HTML: the quoted original becomes a collapsed Quoted block, cid: images stay.</summary>
    IReadOnlyList<Block> ImportWithQuotes(string html);

    /// <summary>The quoted original's HTML with scripts, styles and remote content removed.</summary>
    string CleanQuotedHtml(string html);

    /// <summary>The visible text of an HTML fragment.</summary>
    string HtmlText(string html);
}
