// Mirrors: Rust ui/theme.slint `Theme` inputs (dark, more-contrast, style) and
// design/README.md (one palette, Bento and Nordic styles, dark and light, more-contrast).

namespace Barbican.Presentation.Theme;

/// <summary>
/// One palette selection: legacy Bento/Nordic or Bulwark, each in dark/light and normal/more contrast.
/// </summary>
/// <param name="Dark">Dark theme (Settings › Appearance › Theme).</param>
/// <param name="MoreContrast">Settings › Accessibility › Contrast "More" (Bulwark data-contrast="more").</param>
/// <param name="Nordic">Settings › Appearance › Style: Nordic (one surface, pills and circles) instead of Bento (tiles).</param>
/// <param name="Bulwark">Use the Bulwark default palette instead of a legacy palette.</param>
public readonly record struct ThemeVariant(bool Dark, bool MoreContrast, bool Nordic, bool Bulwark = false)
{
    /// <summary>The fallback before system theme detection: dark, normal contrast, Bulwark.</summary>
    public static ThemeVariant Default => new(Dark: true, MoreContrast: false, Nordic: false, Bulwark: true);

    /// <summary>All twelve palette combinations, in <see cref="Index"/> order.</summary>
    public static IReadOnlyList<ThemeVariant> All { get; } =
        Enumerable.Range(0, 8).Select(i => new ThemeVariant(Dark: (i & 4) == 0, MoreContrast: (i & 2) != 0, Nordic: (i & 1) != 0))
            .Concat(Enumerable.Range(0, 4).Select(i => new ThemeVariant(Dark: i < 2, MoreContrast: (i & 1) != 0, Nordic: false, Bulwark: true))).ToArray();

    /// <summary>Position in <see cref="All"/>; the eight legacy combinations precede Bulwark.</summary>
    public int Index => Bulwark ? 8 + (Dark ? 0 : 2) + (MoreContrast ? 1 : 0) : (Dark ? 0 : 4) + (MoreContrast ? 2 : 0) + (Nordic ? 1 : 0);

    /// <summary>A short stable name, used in generated code and test output.</summary>
    public override string ToString() =>
        $"{(Dark ? "dark" : "light")}-{(MoreContrast ? "more" : "normal")}-{(Bulwark ? "bulwark" : Nordic ? "nordic" : "bento")}";
}
