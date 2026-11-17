// Mirrors: spec/automation-protocol.md `key CHORD` (Enter, Escape, Tab, Up, Down, Ctrl+N ...); the spelling of Rust shortcuts.rs / the help sheet
// ("Ctrl + R", "Shift+G"). Owner mailcore. The `key` IAutomationCommand itself lives in the App (it needs Barbican.Automation, which only the
// Automation configuration references); it calls this parser and then IKeyRouter.Handle with KeySource.Automation.

namespace Barbican.Presentation.Input;

/// <summary>Reads a chord as the automation protocol and the shortcut table write it.</summary>
public static class KeyChordParser
{
    /// <summary>Parses <c>j</c>, <c>Shift+G</c>, <c>Ctrl+K</c>, <c>ctrl+shift+m</c>, <c>Escape</c>; false for an empty or malformed chord.</summary>
    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        string head;
        string key;
        if (string.Equals(trimmed, "+", StringComparison.Ordinal))
        {
            head = string.Empty;
            key = "+";
        }
        else if (trimmed.EndsWith("++", StringComparison.Ordinal))
        {
            // the key is the plus sign itself: "Ctrl++"
            head = trimmed[..^2];
            key = "+";
        }
        else
        {
            var cut = trimmed.LastIndexOf('+');
            head = cut < 0 ? string.Empty : trimmed[..cut];
            key = (cut < 0 ? trimmed : trimmed[(cut + 1)..]).Trim();
        }

        bool control = false, shift = false, alt = false;
        foreach (var part in head.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    control = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                default:
                    return false;
            }
        }

        if (key.Length == 0)
        {
            return false;
        }

        chord = new KeyChord(key, control, shift, alt);
        return true;
    }
}
