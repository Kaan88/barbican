// Mirrors: Fable review 2 R2-05 (Enter on a focused button opened the selected message instead of pressing the button, and shortcuts ran
// behind the sign-in screen); WCAG 2.1.1 (every control works from the keyboard); Bulwark hooks/use-keyboard-shortcuts.ts (shortcuts are
// skipped while the focus is in a field or on a control that takes the key itself). UI-free, so the rule is tested without a window.

using Barbican.Presentation.Input;

namespace Barbican.App.Shell;

/// <summary>Decides whether a real key press goes to the KeyRouter at all.</summary>
public static class KeyGate
{
    /// <param name="chord">The key with its modifiers.</param>
    /// <param name="signInShown">The sign-in screen is up: no mail shortcut applies there.</param>
    /// <param name="focusActivates">The focused element acts on Enter or Space itself (a button, a toggle, a combo box, a list item outside
    /// the message list).</param>
    public static bool Routes(KeyChord chord, bool signInShown, bool focusActivates)
    {
        if (signInShown)
        {
            return false;
        }

        var activation = chord.Key is "Enter" or "Space" && !chord.Control && !chord.Alt;
        return !(activation && focusActivates);
    }

    /// <summary>
    /// Whether an auto-repeated key press (the key held down) may act. Undo never repeats (Bulwark hooks/use-undo-shortcut.ts:17 `e.repeat`): a held
    /// `z` walked back through the whole undo history, putting older messages back into older folders (M2 hotfix 2). Delete does not repeat either:
    /// a held Delete removed message after message.
    /// </summary>
    public static bool RepeatRoutes(KeyChord chord)
    {
        return !(chord.Key is "z" or "Z" or "Delete" or "#");
    }
}
