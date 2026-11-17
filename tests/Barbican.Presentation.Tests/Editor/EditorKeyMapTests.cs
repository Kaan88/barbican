using Barbican.Presentation.Editor;

namespace Barbican.Presentation.Tests.Editor;

/// <summary>The shortcut table (Rust handle_key, spec/menus.md MENU-RTE): chord and mode to command.</summary>
public sealed class EditorKeyMapTests
{
    [Theory]
    [InlineData("B", true, false, false, EditorCommand.Bold)]
    [InlineData("I", true, false, false, EditorCommand.Italic)]
    [InlineData("U", true, false, false, EditorCommand.Underline)]
    [InlineData("E", true, false, false, EditorCommand.Code)]
    [InlineData("S", true, true, false, EditorCommand.Strike)]
    [InlineData("X", true, true, false, EditorCommand.Strike)]
    [InlineData("H", true, true, false, EditorCommand.Highlight)]
    [InlineData("K", true, false, false, EditorCommand.Link)]
    [InlineData("Z", true, false, false, EditorCommand.Undo)]
    [InlineData("Y", true, false, false, EditorCommand.Redo)]
    [InlineData("Z", true, true, false, EditorCommand.Redo)]
    [InlineData("C", true, false, false, EditorCommand.Copy)]
    [InlineData("X", true, false, false, EditorCommand.Cut)]
    [InlineData("V", true, false, false, EditorCommand.Paste)]
    [InlineData("V", true, true, false, EditorCommand.PastePlain)]
    [InlineData("A", true, false, false, EditorCommand.SelectAll)]
    [InlineData("7", true, true, false, EditorCommand.NumberedList)]
    [InlineData("8", true, true, false, EditorCommand.BulletList)]
    [InlineData("B", true, true, false, EditorCommand.Quote)]
    [InlineData("C", true, false, true, EditorCommand.CodeBlock)]
    [InlineData("0", true, false, true, EditorCommand.Paragraph)]
    [InlineData("1", true, false, true, EditorCommand.Heading1)]
    [InlineData("2", true, false, true, EditorCommand.Heading2)]
    [InlineData("3", true, false, true, EditorCommand.Heading3)]
    [InlineData("L", true, true, false, EditorCommand.AlignLeft)]
    [InlineData("E", true, true, false, EditorCommand.AlignCenter)]
    [InlineData("R", true, true, false, EditorCommand.AlignRight)]
    [InlineData("J", true, true, false, EditorCommand.AlignJustify)]
    [InlineData("\\", true, false, false, EditorCommand.ClearFormat)]
    [InlineData(" ", true, false, false, EditorCommand.ClearFormat)]
    [InlineData("]", true, false, false, EditorCommand.Indent)]
    [InlineData("[", true, false, false, EditorCommand.Outdent)]
    [InlineData("Insert", false, true, false, EditorCommand.Paste)]
    [InlineData("Insert", true, false, false, EditorCommand.Copy)]
    [InlineData("Delete", false, true, false, EditorCommand.Cut)]
    public void Chord_resolves_to_its_command(string key, bool ctrl, bool shift, bool alt, EditorCommand expected)
    {
        Assert.Equal(expected, EditorKeyMap.Resolve(new EditorKey(key, ctrl, shift, alt), plain: false, out var swallowed));
        Assert.False(swallowed);
    }

    [Theory]
    [InlineData("B", false, false, false)]
    [InlineData("Q", true, false, false)]
    [InlineData("1", true, false, false)]
    [InlineData("Delete", false, false, false)]
    public void Other_chords_resolve_to_nothing(string key, bool ctrl, bool shift, bool alt)
    {
        Assert.Null(EditorKeyMap.Resolve(new EditorKey(key, ctrl, shift, alt), plain: false, out _));
    }

    [Theory]
    [InlineData("B", true, false, false)]
    [InlineData("K", true, false, false)]
    [InlineData("8", true, true, false)]
    [InlineData("1", true, false, true)]
    public void Plain_text_mode_swallows_formatting_shortcuts(string key, bool ctrl, bool shift, bool alt)
    {
        Assert.Null(EditorKeyMap.Resolve(new EditorKey(key, ctrl, shift, alt), plain: true, out var swallowed));
        Assert.True(swallowed);
    }

    [Theory]
    [InlineData("A", EditorCommand.SelectAll)]
    [InlineData("Z", EditorCommand.Undo)]
    [InlineData("Y", EditorCommand.Redo)]
    [InlineData("C", EditorCommand.Copy)]
    [InlineData("X", EditorCommand.Cut)]
    [InlineData("V", EditorCommand.Paste)]
    public void Plain_text_mode_keeps_the_editing_shortcuts(string key, EditorCommand expected)
    {
        Assert.Equal(expected, EditorKeyMap.Resolve(new EditorKey(key, Ctrl: true), plain: true, out _));
    }

    [Fact]
    public void Every_hint_in_the_menu_resolves_back_to_its_command()
    {
        foreach (var command in Enum.GetValues<EditorCommand>())
        {
            var hint = EditorKeyMap.Hint(command);
            if (hint is null || command is EditorCommand.Redo)
            {
                continue;
            }

            var parts = hint.Split('+');
            var key = new EditorKey(parts[^1], parts.Contains("Ctrl"), parts.Contains("Shift"), parts.Contains("Alt"));
            Assert.True(command == EditorKeyMap.Resolve(key, false, out _), $"{command} {hint}");
        }
    }
}
