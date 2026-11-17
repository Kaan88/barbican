// New tests: R2-11 (Bulwark lib/keyboard.ts:96-101 the typed character wins, :393 layout-aware keys): punctuation comes from the typed character, never from the virtual key, so "/" and "?"
// work on a German layout ("/" is Shift+7 there, "?" is Shift+ß); letters and digits still come from the virtual key. The M1 KeyChordMapperTests (Map) stay as they were. Owner menus.

using Barbican.App.Shell;
using Barbican.Presentation.Input;

namespace Barbican.Presentation.Tests.Input;

public sealed class KeyChordMapperLayoutTests
{
    [Theory]
    [InlineData(111)]
    [InlineData(191)]
    [InlineData(219)]
    [InlineData(186)]
    [InlineData(222)]
    public void Punctuation_virtual_keys_are_left_to_the_typed_character(int virtualKey)
    {
        Assert.Null(KeyChordMapper.MapKeyDown(virtualKey, false, false, false));
        Assert.Null(KeyChordMapper.MapKeyDown(virtualKey, false, true, false));
    }

    [Fact]
    public void On_a_german_layout_shift_7_types_a_slash_and_the_slash_chord_comes_from_the_character()
    {
        // KeyDown: Shift+7 is virtual key 55 with Shift: not the digit (Fable F-16), not a chord
        Assert.Null(KeyChordMapper.MapKeyDown(55, false, true, false));
        // CharacterReceived: the layout typed "/"
        Assert.Equal(new KeyChord("/"), KeyChordMapper.MapCharacter('/', false, false));
        Assert.Equal(new KeyChord("?"), KeyChordMapper.MapCharacter('?', false, false));
        Assert.Equal(new KeyChord("!"), KeyChordMapper.MapCharacter('!', false, false));
    }

    [Theory]
    [InlineData('a')]
    [InlineData('Z')]
    [InlineData('5')]
    [InlineData(' ')]
    [InlineData('\u0001')]
    public void Letters_digits_and_control_characters_never_come_from_the_character_path(char c) => Assert.Null(KeyChordMapper.MapCharacter(c, false, false));

    [Fact]
    public void Ctrl_or_altgr_typing_is_not_a_shortcut()
    {
        Assert.Null(KeyChordMapper.MapCharacter('/', true, false));
        Assert.Null(KeyChordMapper.MapCharacter('€', false, true));
        Assert.Null(KeyChordMapper.MapCharacter('\\', true, true));
    }

    [Fact]
    public void Letters_digits_and_named_keys_still_come_from_the_virtual_key_and_function_keys_are_added()
    {
        Assert.Equal(new KeyChord("j"), KeyChordMapper.MapKeyDown(74, false, false, false));
        Assert.Equal(new KeyChord("k", Control: true), KeyChordMapper.MapKeyDown(75, true, false, false));
        Assert.Equal(new KeyChord("Enter"), KeyChordMapper.MapKeyDown(13, false, false, false));
        Assert.Equal(new KeyChord("F5"), KeyChordMapper.MapKeyDown(116, false, false, false));
        Assert.Equal(new KeyChord("F10", Shift: true), KeyChordMapper.MapKeyDown(121, false, true, false));
        Assert.Equal(new KeyChord("F6"), KeyChordMapper.MapKeyDown(117, false, false, false));
    }

    [Fact]
    public void The_typed_question_mark_reaches_the_sheet_command_through_the_tables()
    {
        var chord = KeyChordMapper.MapCharacter('?', false, false)!.Value;
        Assert.Equal("?", ShortcutCommands.TextOf(chord));
        Assert.Equal(ShortcutAction.Help, ShortcutTables.Resolve(ShortcutPlace.Mail, new ShortcutContext { SingleKeys = true }, ShortcutCommands.TextOf(chord)!, chord.Shift, chord.Control, chord.Alt));
    }
}
