// Mirrors: Rust src/rte/html.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap HTML output), lib/email-html (inline styles); HTML Living Standard 13 (parsing, via AngleSharp). Contract stub from the M3 plan; owner editor-core replaces the bodies.

using Barbican.Core.Common;

namespace Barbican.Core.Editor;

/// <summary>HTML in and out of the document model (port of Rust <c>rte/html.rs</c>). Stub: every member throws <see cref="PortPendingException"/> until stream editor-core lands.</summary>
public static class DocHtml
{
    /// <summary>Tolerant HTML import (AngleSharp DOM): inline marks, blocks, nested lists, tables, images; whitespace collapsed, entities decoded, style/script/comments dropped.</summary>
    public static IReadOnlyList<Block> Import(string html) => throw new PortPendingException("DocHtml.Import");

    /// <summary>Plain text to paragraph blocks.</summary>
    public static IReadOnlyList<Block> ImportText(string text) => throw new PortPendingException("DocHtml.ImportText");

    /// <summary>A whole message body (drafts): a top-level blockquote type=cite becomes the collapsible quoted block again.</summary>
    public static IReadOnlyList<Block> ImportWithQuotes(string html) => throw new PortPendingException("DocHtml.ImportWithQuotes");

    /// <summary>Bulwark's exact email-safe inline-style HTML in the wrapper div (hand-written, snapshot-tested).</summary>
    public static string Export(Doc doc) => throw new PortPendingException("DocHtml.Export");

    /// <summary>The text/plain alternative.</summary>
    public static string ToText(Doc doc) => throw new PortPendingException("DocHtml.ToText");

    /// <summary>HTML escaping as the exporter does it (two spaces become space plus nbsp).</summary>
    public static string Esc(string text) => throw new PortPendingException("DocHtml.Esc");

    /// <summary>Named and numeric entities.</summary>
    public static string DecodeEntities(string text) => throw new PortPendingException("DocHtml.DecodeEntities");

    /// <summary>A CSS colour (rgb(), #rgb, #rrggbb) as #rrggbb, or null.</summary>
    public static string? NormColor(string value) => throw new PortPendingException("DocHtml.NormColor");

    /// <summary>Drops document wrappers, head, styles and scripts so a quote cannot restyle the reply.</summary>
    public static string CleanQuotedHtml(string html) => throw new PortPendingException("DocHtml.CleanQuotedHtml");

    /// <summary>Plain text of an HTML fragment (the collapsed quote and text/plain).</summary>
    public static string HtmlText(string html) => throw new PortPendingException("DocHtml.HtmlText");

    /// <summary>Index of the first quoted block: the source view edits what comes before it.</summary>
    public static int EditableEnd(Doc doc) => throw new PortPendingException("DocHtml.EditableEnd");

    /// <summary>The editable part as readable HTML, one block per line, without the wrapper div.</summary>
    public static string Source(Doc doc) => throw new PortPendingException("DocHtml.Source");

    /// <summary>Line breaks after block-level tags.</summary>
    public static string Pretty(string html) => throw new PortPendingException("DocHtml.Pretty");

    /// <summary>The document after editing the source: the new editable part (keeping uploaded image data) plus the untouched quoted tail.</summary>
    public static Doc ApplySource(Doc doc, string source) => throw new PortPendingException("DocHtml.ApplySource");
}
