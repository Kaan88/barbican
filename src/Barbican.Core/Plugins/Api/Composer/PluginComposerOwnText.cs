// Mirrors: lib/plugin-sandbox/composer-own-text.ts:1-80 (ownRange: the user's own text ends at the signature or the quote, blank lines after it stay,
// an empty draft gets its first empty paragraph) and composer-edit.ts:38-137 (readOwnContent, replaceOwnContent keeps inline images, replaceSelection refuses protected text).
// The native document marks the signature and the reply header with Run.IsComposerContext and holds the quote as one Quoted block.
using Barbican.Core.Editor;

namespace Barbican.Core.Plugins.Api.Composer;

/// <summary>Where the user's own text is in a composer document, and edits that leave the signature, the quote and inline images in place.</summary>
public static class PluginComposerOwnText
{
    /// <summary>Block indexes [From, To) of the user's own text; a collapsed range is where text written into an empty draft goes.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct OwnRange(int From, int To);

    public static bool IsProtected(Block block) =>
        block.Kind is BlockKind.Quoted || (block.Runs.Count > 0 && !block.IsEmpty && block.Runs.All(run => run.IsComposerContext));

    public static OwnRange Range(Doc doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var boundary = doc.Blocks.ToList().FindIndex(IsProtected);
        if (boundary < 0) boundary = doc.Blocks.Count;
        var last = boundary - 1;
        while (last >= 0 && IsBlank(doc.Blocks[last])) last--;
        if (last >= 0) return new(0, last + 1);
        return boundary > 0 ? new(0, 1) : new(boundary, boundary);
    }

    /// <summary>The own text as editor HTML and trimmed plain text.</summary>
    public static PluginComposerBody Read(Doc doc)
    {
        var range = Range(doc);
        var own = new Doc(doc.Blocks.Skip(range.From).Take(range.To - range.From).Select(block => block.Clone()));
        if (own.Blocks.Count == 0) return new(string.Empty, string.Empty);
        return new(Inner(DocHtml.Export(own)), DocHtml.ToText(own).Trim());
    }

    /// <summary>The document with its own text replaced. Inline images of the replaced range follow the new text.</summary>
    public static Doc Replace(Doc doc, IReadOnlyList<Block> content)
    {
        var range = Range(doc);
        var blocks = doc.Blocks.Take(range.From).Select(block => block.Clone()).ToList();
        blocks.AddRange(content);
        blocks.AddRange(doc.Blocks.Skip(range.From).Take(range.To - range.From)
            .Where(block => block.Kind is BlockKind.Image).Select(block => block.Clone()));
        blocks.AddRange(doc.Blocks.Skip(range.To).Select(block => block.Clone()));
        var result = new Doc(blocks) { HasComposerProvenance = doc.HasComposerProvenance };
        result.Fix();
        return result;
    }

    /// <summary>The selected blocks as editor HTML.</summary>
    public static string Html(IReadOnlyList<Block> blocks) =>
        blocks.Count == 0 ? string.Empty : Inner(DocHtml.Export(new Doc(blocks.Select(block => block.Clone()))));

    private static bool IsBlank(Block block) =>
        block.Kind is BlockKind.Para or BlockKind.Quote or BlockKind.CodeBlock or BlockKind.Heading or BlockKind.Bullet or BlockKind.Ordered && block.Text.Trim().Length == 0;

    private static string Inner(string html) =>
        html.StartsWith(HtmlExporter.WrapOpen, StringComparison.Ordinal) && html.EndsWith("</div>", StringComparison.Ordinal)
            ? html[HtmlExporter.WrapOpen.Length..^"</div>".Length] : html;
}
