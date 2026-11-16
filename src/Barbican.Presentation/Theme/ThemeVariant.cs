// Mirrors: Rust ui/theme.slint `Theme` inputs (dark, more-contrast, style) and
// design/README.md (one palette, Bento and Nordic styles, dark and light, more-contrast).

namespace Barbican.Presentation.Theme;

/// <summary>
/// One combination of the three appearance switches. The palette has a value for every one of
/// the eight combinations (<see cref="Tokens.Palette(ThemeVariant)"/>).
/// </summary>
/// <param name="Dark">Dark theme (Settings › Appearance › Theme).</param>
/// <param name="MoreContrast">Settings › Accessibility › Contrast "More" (Bulwark data-contrast="more").</param>
/// <param name="Nordic">Settings › Appearance › Style: Nordic (one surface, pills and circles) instead of Bento (tiles).</param>
public readonly record struct ThemeVariant(bool Dark, bool MoreContrast, bool Nordic)
{
    /// <summary>The app's first-run look: dark, normal contrast, Bento.</summary>
    public static ThemeVariant Default => new(Dark: true, MoreContrast: false, Nordic: false);

    /// <summary>All eight combinations, in <see cref="Index"/> order.</summary>
    public static IReadOnlyList<ThemeVariant> All { get; } =
        Enumerable.Range(0, 8).Select(i => new ThemeVariant(Dark: (i & 4) == 0, MoreContrast: (i & 2) != 0, Nordic: (i & 1) != 0)).ToArray();

    /// <summary>Position in <see cref="All"/>: dark first, then contrast, then style.</summary>
    public int Index => (Dark ? 0 : 4) + (MoreContrast ? 2 : 0) + (Nordic ? 1 : 0);

    /// <summary>A short stable name, used in generated code and test output.</summary>
    public override string ToString() =>
        $"{(Dark ? "dark" : "light")}-{(MoreContrast ? "more" : "normal")}-{(Nordic ? "nordic" : "bento")}";
}
