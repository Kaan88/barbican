// Mirrors: Rust shortcuts.rs (Slint key text with shift applied, ctrl, alt); Bulwark hooks/use-keyboard-shortcuts.ts; the KeyChord spelling of Barbican.Presentation.Input.
// UI-free (virtual-key numbers), so the mapping is tested without a window.

using Barbican.Presentation.Input;

namespace Barbican.App.Shell;

/// <summary>Turns a virtual key (Windows.System.VirtualKey value) and the held modifiers into the chord the KeyRouter reads; null for a key the router never handles.</summary>
public static class KeyChordMapper
{
    public static KeyChord? Map(int virtualKey, bool control, bool shift, bool alt)
    {
        if (control && alt)
        {
            // AltGr reports Ctrl and Alt together and types a character on many layouts: not a shortcut (Fable F-16)
            return null;
        }

        if (shift && virtualKey is >= 48 and <= 57)
        {
            // Shift+digit types a layout-dependent character ("!" or "/" ...): the digit is not what was pressed (Fable F-16)
            return null;
        }

        string? key = virtualKey switch
        {
            >= 65 and <= 90 => ((char)(shift ? virtualKey : virtualKey + 32)).ToString(),
            >= 48 and <= 57 => ((char)virtualKey).ToString(),
            >= 96 and <= 105 => ((char)('0' + (virtualKey - 96))).ToString(),
            13 => "Enter",
            27 => "Escape",
            38 => "ArrowUp",
            40 => "ArrowDown",
            37 => "ArrowLeft",
            39 => "ArrowRight",
            36 => "Home",
            35 => "End",
            46 => "Delete",
            9 => "Tab",
            32 => "Space",
            111 or 191 => shift ? "?" : "/",
            _ => null,
        };
        return key is null ? null : new KeyChord(key, control, shift, alt);
    }

    /// <summary>
    /// The chord of a real KeyDown (R2-11, Bulwark lib/keyboard.ts:96-101 and :393): letters, digits and named keys come from the virtual key, punctuation never does (a virtual key
    /// is not the character on a German or French layout: "/" is Shift+7 there), so every OEM key and the numpad divide return null here and arrive through <see cref="MapCharacter"/>.
    /// The function keys the shortcut tables read (F2, F5, F6, F9, F10) are added.
    /// </summary>
    public static KeyChord? MapKeyDown(int virtualKey, bool control, bool shift, bool alt)
    {
        if (virtualKey is 111 or (>= 186 and <= 222))
        {
            return null;
        }

        var function = virtualKey switch { 113 => "F2", 116 => "F5", 117 => "F6", 120 => "F9", 121 => "F10", _ => null };
        if (function is not null)
        {
            return new KeyChord(function, control, shift, alt);
        }

        return Map(virtualKey, control, shift, alt);
    }

    /// <summary>The chord of a typed printable punctuation character ("/", "?", "!", "#"), as the layout produced it; null for letters, digits, control characters and AltGr or Ctrl typing.</summary>
    public static KeyChord? MapCharacter(char character, bool control, bool alt)
    {
        if (control || alt || character is <= ' ' or > '~' || char.IsAsciiLetterOrDigit(character))
        {
            return null;
        }

        return new KeyChord(character.ToString());
    }
}
