// Mirrors: Bulwark components/global-search/search-preview.tsx:57-70 (DetailRow: a muted leading icon, a wrapping value).

namespace Barbican.Presentation.Palette;

/// <summary>One detail line of the palette's preview.</summary>
/// <param name="Icon">The palette icon key (mail, clock, pin, people, phone, building, folder, drive, text).</param>
/// <param name="Text">The value.</param>
public sealed record PalettePreviewLine(string Icon, string Text);
