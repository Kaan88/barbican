// Mirrors: PORT-PLAN.md §3.7 (palette brushes are rewritten in place for theme, contrast and style; nothing is re-templated, nothing flashes);
// prototype/winui Palette.Apply.

namespace Barbican.App.Theme;

/// <summary>
/// Writes a palette into slots that already exist (the brushes of Tokens.xaml). It never creates or replaces a slot, and it writes only
/// a value that changed, so a theme flip touches each brush once and an identical palette touches none.
/// </summary>
public static class PaletteWriter
{
    /// <summary>Writes every key of <paramref name="palette"/> whose current value differs; returns the keys it wrote.</summary>
    /// <param name="palette">key to 0xAARRGGBB.</param>
    /// <param name="read">The current value of a slot, or null when the slot does not exist.</param>
    /// <param name="write">Sets a slot.</param>
    public static IReadOnlyList<string> Write(IReadOnlyDictionary<string, uint> palette, Func<string, uint?> read, Action<string, uint> write)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        var written = new List<string>();
        foreach (var (key, argb) in palette)
        {
            if (read(key) is not { } current)
            {
                throw new InvalidOperationException($"No palette slot '{key}': Tokens.xaml and Tokens.Palette disagree (run tools/build.ps1 gen-tokens).");
            }

            if (current != argb)
            {
                write(key, argb);
                written.Add(key);
            }
        }

        return written;
    }
}
