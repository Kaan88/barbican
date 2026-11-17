// Mirrors: Rust ctxmenu.rs submenu "tag" and "color" :575; Bulwark components/layout/sidebar.tsx tag rows. Owner menus.

namespace Barbican.Presentation.Menus;

/// <summary>A tag as a menu needs it.</summary>
/// <param name="Id">The tag id (the keyword is <c>$label:id</c>).</param>
/// <param name="Label">The name shown.</param>
/// <param name="Color">The colour name in the tag palette.</param>
/// <param name="ColorRgb">The colour 0xRRGGBB.</param>
public sealed record MenuTag(string Id, string Label, string Color, uint ColorRgb);
