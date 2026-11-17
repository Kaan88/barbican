// Mirrors: see IDocCodec; delegates to editor-core's DocHtml. Owner send-pipeline.

using Barbican.Core.Editor;

namespace Barbican.Core.Compose;

/// <summary>The default <see cref="IDocCodec"/>: <see cref="DocHtml"/>.</summary>
public sealed class DocHtmlCodec : IDocCodec
{
    public string Export(Doc doc) => DocHtml.Export(doc);

    public string ToText(Doc doc) => DocHtml.ToText(doc);

    public IReadOnlyList<Block> Import(string html) => DocHtml.Import(html);

    public IReadOnlyList<Block> ImportText(string text) => DocHtml.ImportText(text);

    public IReadOnlyList<Block> ImportWithQuotes(string html) => DocHtml.ImportWithQuotes(html);

    public string CleanQuotedHtml(string html) => DocHtml.CleanQuotedHtml(html);

    public string HtmlText(string html) => DocHtml.HtmlText(html);
}
