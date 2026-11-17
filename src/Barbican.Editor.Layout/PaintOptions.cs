// Mirrors: Rust src/rte/layout.rs Painted :50 (what a frame draws). No Bulwark/RFC/Stalwart counterpart.

using Barbican.Core.Editor.Spell;

namespace Barbican.Editor.Layout;

/// <summary>What one paint pass shows besides the document: the visible slice, spelling marks and the selected image.</summary>
public sealed record PaintOptions
{
    /// <summary>Top of the visible slice in document coordinates; blocks wholly outside [ClipTop, ClipBottom] are not drawn. Null draws everything.</summary>
    public float? ClipTop { get; init; }

    public float? ClipBottom { get; init; }

    /// <summary>Misspelt ranges of a block's text (UTF-16), asked once per visible block; null draws no marks.</summary>
    public Func<int, string, IReadOnlyList<SpellRange>>? SpellMarks { get; init; }

    /// <summary>The block index of an image to outline as selected, or -1.</summary>
    public int SelectedImage { get; init; } = -1;
}
