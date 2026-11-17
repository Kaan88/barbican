// Mirrors: Rust shortcuts.rs (Slint key text with shift applied, ctrl, alt); Bulwark hooks/use-keyboard-shortcuts.ts; the KeyChord spelling of Barbican.Presentation.Input.
// UI-free (virtual-key numbers), so the mapping is tested without a window.

using Barbican.Presentation.Input;

namespace Barbican.App.Shell;

/// <summary>Turns a virtual key (Windows.System.VirtualKey value) and the held modifiers into the chord the KeyRouter reads; null for a key the router never handles.</summary>
public static class KeyChordMapper
{
    public static KeyChord? Map(int virtualKey, bool control, bool shift, bool alt)
    {
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
}
