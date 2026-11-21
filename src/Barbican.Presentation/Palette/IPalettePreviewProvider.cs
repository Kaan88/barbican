// Mirrors: Bulwark components/global-search/search-preview.tsx:158-205 (the preview of the selected hit).

namespace Barbican.Presentation.Palette;

/// <summary>A palette provider that shows a preview of the highlighted row.</summary>
public interface IPalettePreviewProvider : IPaletteProvider
{
    /// <summary>The preview of a row this provider returned, or null for a row it does not know (a plugin's row).</summary>
    PalettePreview? PreviewFor(PaletteCommand row);
}
