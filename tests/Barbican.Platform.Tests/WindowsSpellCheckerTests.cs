#if WINDOWS
#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature (the ported Rust asserts)
using Barbican.Core.Editor.Spell;
using Barbican.Platform.Windows.Spelling;

namespace Barbican.Platform.Tests;

/// <summary>Rust src/rte/spell.rs windows_checker, ported (M3 stream editor-core): the Windows spell checker finds misspelt words. Returns quietly when the machine has no English dictionary, as Rust did.</summary>
public sealed class WindowsSpellCheckerTests
{
    [Fact]
    [Red("editor-core")]
    [RustTest("rte::spell::windows_checker")]
    public void Windows_checker()
    {
        var c = new WindowsSpellCheckerFactory().Create("en-US");
        if (c is null)
        {
            return;
        }

        const string t = "This sentense has a mispelled word.";
        var words = SpellText.ToTextRanges(t, c.Check(t)).Select(r => t[r.Start..r.End]).ToList();
        Assert.Contains("sentense", words);
        Assert.Contains("mispelled", words);
        Assert.Contains("sentence", c.Suggest("sentense"));
    }
}
#endif
