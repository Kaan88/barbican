using Barbican.App.Theme;
using Barbican.Presentation.Theme;

namespace Barbican.Presentation.Tests.ShellUi;

/// <summary>PORT-PLAN.md §3.7: a theme, style or contrast change rewrites the brushes that exist; it never replaces or re-creates one.</summary>
public sealed class PaletteWriterTests
{
    private static Dictionary<string, uint> SlotsOf(ThemeVariant v) => Tokens.Palette(v).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    [Fact]
    public void AFlipWritesTheSameSlotsInPlace()
    {
        var slots = SlotsOf(new ThemeVariant(true, false, false));
        var keysBefore = slots.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var target = new ThemeVariant(false, false, false);
        var written = PaletteWriter.Write(Tokens.Palette(target), k => slots.TryGetValue(k, out var v) ? v : null, (k, v) => slots[k] = v);
        Assert.NotEmpty(written);
        Assert.True(keysBefore.SequenceEqual(slots.Keys.OrderBy(k => k, StringComparer.Ordinal), StringComparer.Ordinal));
        Assert.All(Tokens.Palette(target), p => Assert.Equal(p.Value, slots[p.Key]));
    }

    [Fact]
    public void TheSamePaletteWritesNothing()
    {
        var v = new ThemeVariant(true, true, true);
        var slots = SlotsOf(v);
        Assert.Empty(PaletteWriter.Write(Tokens.Palette(v), k => slots[k], (k, x) => slots[k] = x));
    }

    [Fact]
    public void AMissingSlotIsAnErrorNotACreation()
    {
        var slots = new Dictionary<string, uint>(StringComparer.Ordinal);
        Assert.Throws<InvalidOperationException>(() => PaletteWriter.Write(Tokens.Palette(new ThemeVariant(true, false, false)), k => slots.TryGetValue(k, out var v) ? v : null, (k, x) => slots[k] = x));
        Assert.Empty(slots);
    }

    [Fact]
    public void EveryVariantHasTheSameKeys()
    {
        var reference = SlotsOf(new ThemeVariant(true, false, false)).Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        foreach (var dark in new[] { true, false })
        {
            foreach (var more in new[] { true, false })
            {
                foreach (var nordic in new[] { true, false })
                {
                    Assert.True(reference.SequenceEqual(SlotsOf(new ThemeVariant(dark, more, nordic)).Keys.OrderBy(k => k, StringComparer.Ordinal), StringComparer.Ordinal));
                }
            }
        }
    }

    [Fact]
    public void EveryPaletteKeyIsABrushInTokensXaml()
    {
        var xaml = File.ReadAllText(TestRepo.PathOf("src/Barbican.App/Themes/Tokens.xaml"));
        foreach (var key in SlotsOf(new ThemeVariant(true, false, false)).Keys)
        {
            Assert.Contains($"x:Key=\"{key}\"", xaml, StringComparison.Ordinal);
        }
    }
}
