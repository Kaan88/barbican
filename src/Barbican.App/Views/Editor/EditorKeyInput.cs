// Mirrors: Rust src/rte/mod.rs on_key :517 (a key arrives as text plus ctrl, shift, alt); spec/automation-protocol.md `key CHORD` (the same chord spelling for the editor probes).
// UI-free (virtual-key numbers and chord text), so the mapping is tested without a window.

using System.Globalization;
using Barbican.Editor.Layout.Input;
using Barbican.Presentation.Editor;

namespace Barbican.App.Views.Editor;

/// <summary>What a key press means to the editor: an editing shortcut, a navigation key, or neither (the hidden text box then takes it as text).</summary>
public readonly record struct EditorKeyInput(EditorKey Key, NavKey? Nav)
{
    public bool Ctrl => Key.Ctrl;

    public bool Shift => Key.Shift;

    public bool Alt => Key.Alt;

    /// <summary>The input of a virtual key (Windows.System.VirtualKey value) with the held modifiers; null for keys the editor leaves to the text box (letters without Ctrl, digits, punctuation).</summary>
    public static EditorKeyInput? FromVirtualKey(int virtualKey, bool ctrl, bool shift, bool alt)
    {
        var nav = virtualKey switch
        {
            37 => NavKey.Left,
            39 => NavKey.Right,
            38 => NavKey.Up,
            40 => NavKey.Down,
            36 => NavKey.Home,
            35 => NavKey.End,
            33 => NavKey.PageUp,
            34 => NavKey.PageDown,
            8 => NavKey.Backspace,
            46 => NavKey.Delete,
            13 => NavKey.Enter,
            9 => NavKey.Tab,
            27 => NavKey.Escape,
            _ => (NavKey?)null,
        };
        if (nav is { } n)
        {
            return new EditorKeyInput(new EditorKey(n.ToString(), ctrl, shift, alt), n);
        }

        var name = virtualKey switch
        {
            45 => "Insert",
            >= 65 and <= 90 => ((char)virtualKey).ToString(),
            >= 48 and <= 57 => ((char)virtualKey).ToString(),
            32 => " ",
            219 => "[",
            221 => "]",
            220 => "\\",
            _ => null,
        };
        if (name is null || (!ctrl && name is not "Insert"))
        {
            return null;
        }

        return new EditorKeyInput(new EditorKey(name, ctrl, shift, alt), null);
    }

    /// <summary>Parses "ctrl+shift+7", "left", "shift+End", "ctrl+b", "enter", "alt+ctrl+1", "esc" (case-insensitive); null for an unknown key name.</summary>
    public static EditorKeyInput? Parse(string chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        bool ctrl = false, shift = false, alt = false;
        var parts = chord.Trim().Split('+', StringSplitOptions.TrimEntries);
        foreach (var modifier in parts.Take(parts.Length - 1))
        {
            switch (modifier.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    ctrl = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                default:
                    return null;
            }
        }

        var last = parts[^1];
        var nav = last.ToLowerInvariant() switch
        {
            "left" or "arrowleft" => NavKey.Left,
            "right" or "arrowright" => NavKey.Right,
            "up" or "arrowup" => NavKey.Up,
            "down" or "arrowdown" => NavKey.Down,
            "home" => NavKey.Home,
            "end" => NavKey.End,
            "pageup" => NavKey.PageUp,
            "pagedown" => NavKey.PageDown,
            "backspace" => NavKey.Backspace,
            "delete" or "del" => NavKey.Delete,
            "enter" or "return" => NavKey.Enter,
            "tab" => NavKey.Tab,
            "escape" or "esc" => NavKey.Escape,
            _ => (NavKey?)null,
        };
        if (nav is { } n)
        {
            return new EditorKeyInput(new EditorKey(n.ToString(), ctrl, shift, alt), n);
        }

        var name = last.ToLowerInvariant() switch
        {
            "insert" => "Insert",
            "space" => " ",
            _ => last.Length == 1 ? last.ToUpperInvariant() : null,
        };
        return name is null ? null : new EditorKeyInput(new EditorKey(name, ctrl, shift, alt), null);
    }

    /// <summary>The chord text for logs and the dump.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{(Ctrl ? "ctrl+" : string.Empty)}{(Shift ? "shift+" : string.Empty)}{(Alt ? "alt+" : string.Empty)}{Key.Name}");
}
