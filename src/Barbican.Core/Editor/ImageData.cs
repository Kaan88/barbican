// Mirrors: Rust src/rte/model.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap schema).
// No RFC or Stalwart counterpart: the composer document model is client-side only.

namespace Barbican.Core.Editor;

/// <summary>An inline image: a <c>cid:</c> attachment once uploaded, or a remote or data URL kept as is.</summary>
public sealed record ImageData
{
    public string Src { get; init; } = "";

    public string? Cid { get; init; }

    public string? BlobId { get; init; }

    public string Mime { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>Display width in px (0 = natural, capped by the editor width).</summary>
    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Encoded bytes for the preview (pasted or inserted images); compared by reference.</summary>
    public byte[]? Bytes { get; init; }

    public string Alt { get; init; } = "";
}
