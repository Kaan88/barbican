// Mirrors: Rust src/rte/model.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap schema).
// No RFC or Stalwart counterpart: the composer document model is client-side only.

namespace Barbican.Core.Editor;

/// <summary>
/// What a block is. Rust's <c>Kind</c> enum with data becomes a closed record hierarchy: compare
/// with <c>==</c> (value equality) or pattern-match on the nested types.
/// </summary>
public abstract record BlockKind
{
    private BlockKind()
    {
    }

    /// <summary>Atomic blocks hold no runs and are selected as a whole.</summary>
    public bool IsAtomic => this is Image or Quoted or Rule;

    public bool IsList => this is Bullet or Ordered;

    public bool IsCell => this is Cell;

    /// <summary>The table id of a cell, otherwise null.</summary>
    public int? Table => this is Cell c ? c.TableId : null;

    public sealed record Para : BlockKind;

    public sealed record Heading(int Level) : BlockKind;

    public sealed record Quote : BlockKind;

    public sealed record CodeBlock : BlockKind;

    public sealed record Bullet : BlockKind;

    public sealed record Ordered : BlockKind;

    /// <summary>A table cell. A table is consecutive cells (row-major) sharing <see cref="TableId"/>.</summary>
    public sealed record Cell(int TableId, int Row, int Col, bool Header) : BlockKind;

    public sealed record Image(ImageData Data) : BlockKind;

    /// <summary>
    /// The quoted original of a reply or forward, kept as HTML and shown collapsed. <c>Cite</c>:
    /// a reply quote (blockquote, "&gt; " in text/plain); false: a forwarded body.
    /// </summary>
    public sealed record Quoted(string Html, string Text, bool Open, bool Cite) : BlockKind;

    public sealed record Rule : BlockKind;
}
