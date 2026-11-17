using Barbican.App.Views.Editor;
using Barbican.Editor.Layout.Input;

namespace Barbican.Presentation.Tests.Editor;

/// <summary>Virtual keys and chord text to editor key input (the same table a real KeyDown and the editor-key probe use).</summary>
public sealed class EditorKeyInputTests
{
    [Theory]
    [InlineData(37, NavKey.Left)]
    [InlineData(39, NavKey.Right)]
    [InlineData(38, NavKey.Up)]
    [InlineData(40, NavKey.Down)]
    [InlineData(36, NavKey.Home)]
    [InlineData(35, NavKey.End)]
    [InlineData(33, NavKey.PageUp)]
    [InlineData(34, NavKey.PageDown)]
    [InlineData(8, NavKey.Backspace)]
    [InlineData(46, NavKey.Delete)]
    [InlineData(13, NavKey.Enter)]
    [InlineData(9, NavKey.Tab)]
    [InlineData(27, NavKey.Escape)]
    public void Navigation_keys_map_with_their_modifiers(int vk, NavKey expected)
    {
        var input = EditorKeyInput.FromVirtualKey(vk, ctrl: true, shift: true, alt: false);
        Assert.Equal(expected, input?.Nav);
        Assert.True(input?.Ctrl);
        Assert.True(input?.Shift);
    }

    [Fact]
    public void Letters_and_digits_are_shortcuts_only_with_ctrl_and_otherwise_text()
    {
        Assert.Null(EditorKeyInput.FromVirtualKey(66, false, false, false));
        Assert.Null(EditorKeyInput.FromVirtualKey(55, false, true, false));
        Assert.Null(EditorKeyInput.FromVirtualKey(32, false, false, false));
        var b = EditorKeyInput.FromVirtualKey(66, true, false, false);
        Assert.Equal("B", b?.Key.Name);
        Assert.Null(b?.Nav);
        Assert.Equal("7", EditorKeyInput.FromVirtualKey(55, true, true, false)?.Key.Name);
        Assert.Equal("\\", EditorKeyInput.FromVirtualKey(220, true, false, false)?.Key.Name);
        Assert.Equal("Insert", EditorKeyInput.FromVirtualKey(45, false, true, false)?.Key.Name);
    }

    [Theory]
    [InlineData("ctrl+b", "B", true, false, false)]
    [InlineData("Ctrl+Shift+7", "7", true, true, false)]
    [InlineData("alt+ctrl+1", "1", true, false, true)]
    [InlineData("ctrl+space", " ", true, false, false)]
    [InlineData("ctrl+]", "]", true, false, false)]
    public void Chord_text_parses_to_a_key(string chord, string name, bool ctrl, bool shift, bool alt)
    {
        var input = EditorKeyInput.Parse(chord);
        Assert.Equal((name, ctrl, shift, alt), (input?.Key.Name, input?.Ctrl, input?.Shift, input?.Alt));
    }

    [Theory]
    [InlineData("left", NavKey.Left)]
    [InlineData("shift+End", NavKey.End)]
    [InlineData("esc", NavKey.Escape)]
    [InlineData("ctrl+Right", NavKey.Right)]
    [InlineData("PageDown", NavKey.PageDown)]
    public void Chord_text_parses_to_navigation(string chord, NavKey expected)
    {
        Assert.Equal(expected, EditorKeyInput.Parse(chord)?.Nav);
    }

    [Theory]
    [InlineData("hyper+b")]
    [InlineData("ctrl+nonsense")]
    public void Unknown_chords_are_refused(string chord)
    {
        Assert.Null(EditorKeyInput.Parse(chord));
    }
}
