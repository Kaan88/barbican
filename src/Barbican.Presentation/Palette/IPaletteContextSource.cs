// Mirrors: Rust palette.rs context :230 (the palette reads where it was opened and what is true there). Owner menus.

namespace Barbican.Presentation.Palette;

/// <summary>Reads the state the palette depends on, each time it opens.</summary>
public interface IPaletteContextSource
{
    PaletteContext Current();
}
