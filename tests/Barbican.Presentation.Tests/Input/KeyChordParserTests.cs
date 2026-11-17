// New tests: the chord spelling of the automation `key` command and the shortcut table. Owner mailcore.

using Barbican.Presentation.Input;

namespace Barbican.Presentation.Tests.Input;

public sealed class KeyChordParserTests
{
    [Theory]
    [InlineData("j", "j", false, false, false)]
    [InlineData("Shift+G", "G", false, true, false)]
    [InlineData("ctrl+shift+m", "m", true, true, false)]
    [InlineData("Ctrl + K", "K", true, false, false)]
    [InlineData("Escape", "Escape", false, false, false)]
    [InlineData("Down", "Down", false, false, false)]
    [InlineData("/", "/", false, false, false)]
    [InlineData("Ctrl++", "+", true, false, false)]
    [InlineData("Alt+Left", "Left", false, false, true)]
    public void Chords_are_read(string text, string key, bool ctrl, bool shift, bool alt)
    {
        Assert.True(KeyChordParser.TryParse(text, out var chord));
        Assert.Equal(new KeyChord(key, ctrl, shift, alt), chord);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Hyper+J")]
    [InlineData("Ctrl+")]
    public void Malformed_chords_are_refused(string text)
    {
        Assert.False(KeyChordParser.TryParse(text, out _));
    }
}
