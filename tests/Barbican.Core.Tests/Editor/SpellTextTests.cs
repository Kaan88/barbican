using Barbican.Core.Editor.Spell;

namespace Barbican.Core.Tests.Editor;

/// <summary>Rust src/rte/spell.rs tests tags, utf16_ranges and skips, ported. utf16_ranges changed with a D- entry (D-240): offsets are UTF-16 already.</summary>
public sealed class SpellTextTests
{
    [Fact]
    [RustTest("rte::spell::tags")]
    public void Tags()
    {
        Assert.Equal("de-DE", SpellText.LanguageTag("de", "en-GB"));
        Assert.Equal("en-GB", SpellText.LanguageTag("auto", "en-GB"));
        Assert.Equal("en-US", SpellText.LanguageTag("auto", ""));
    }

    [Fact]
    [RustTest("rte::spell::utf16_ranges")]
    public void Utf16_ranges()
    {
        // the checker reports (start, length) in UTF-16 units, which are the string's own indices: they pass through, bad ones are dropped (D-240)
        const string t = "é \U0001F600 wrng";
        Assert.Equal(9, t.Length);
        Assert.Equal([(5, 9)], SpellText.ToTextRanges(t, [new SpellRange(5, 4)]));
        Assert.Equal([(0, 1)], SpellText.ToTextRanges(t, [new SpellRange(0, 1)]));
        Assert.Empty(SpellText.ToTextRanges(t, [new SpellRange(8, 9)]));
        Assert.Empty(SpellText.ToTextRanges(t, [new SpellRange(3, 0)]));
    }

    [Fact]
    [RustTest("rte::spell::skips")]
    public void Skips()
    {
        Assert.True(SpellText.SkipWord("https://x.org/abc"));
        Assert.True(SpellText.SkipWord("me@x.example"));
        Assert.True(SpellText.SkipWord("abc123"));
        Assert.False(SpellText.SkipWord("helo"));
        Assert.Equal("https://x.org/qq", SpellText.Token("see https://x.org/qq now", 14, 16));
    }
}
