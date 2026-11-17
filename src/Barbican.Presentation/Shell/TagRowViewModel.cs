// Mirrors: Rust tags.rs Row :139 and mail.rs rebuild_tags; Bulwark components/layout/sidebar.tsx (keyword list); spec I-044..I-063. Owner mailcore.

namespace Barbican.Presentation.Shell;

/// <summary>A row of the sidebar's tag list.</summary>
/// <param name="Id">The tag id (the keyword is <c>$label:id</c>).</param>
/// <param name="Label">The name shown (an orphan is named in full).</param>
/// <param name="ColorRgb">The tag colour 0xRRGGBB, in the colour-blind-safe palette when that setting is on.</param>
/// <param name="Depth">Nesting level.</param>
/// <param name="Chevron">0 none, 1 open, 2 closed.</param>
public sealed record TagRowViewModel(string Id, string Label, uint ColorRgb, int Depth, int Chevron);
