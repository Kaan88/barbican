// Mirrors: PORT-PLAN.md §3.7; prototype/winui Palette.Apply (rewrites the shared brushes in place, so every element that uses them follows live).

using Barbican.Presentation.Theme;

namespace Barbican.App.Theme;

/// <summary>Applies a palette variant to the application's brushes in place: the brush instances never change, only their colours.</summary>
public static class PaletteApplier
{
    /// <summary>Writes the palette of <paramref name="variant"/> into the brushes of the application resources; returns the keys that changed.</summary>
    public static IReadOnlyList<string> Apply(ThemeVariant variant)
    {
        var resources = Application.Current.Resources;
        return PaletteWriter.Write(
            Tokens.Palette(variant),
            key => resources.TryGetValue(key, out var o) && o is SolidColorBrush b ? AppResources.ToArgb(b.Color) : null,
            (key, argb) => ((SolidColorBrush)resources[key]).Color = AppResources.FromArgb(argb));
    }
}
