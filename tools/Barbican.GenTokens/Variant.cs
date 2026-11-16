namespace Barbican.GenTokens;

/// <summary>The eight palette variants, in the order Barbican.Presentation.Theme.ThemeVariant.Index uses.</summary>
public readonly record struct Variant(bool Dark, bool MoreContrast, bool Nordic)
{
    public static IReadOnlyList<Variant> All { get; } =
        Enumerable.Range(0, 8).Select(i => new Variant(Dark: (i & 4) == 0, MoreContrast: (i & 2) != 0, Nordic: (i & 1) != 0)).ToArray();

    public override string ToString() =>
        $"{(Dark ? "dark" : "light")}-{(MoreContrast ? "more" : "normal")}-{(Nordic ? "nordic" : "bento")}";
}
