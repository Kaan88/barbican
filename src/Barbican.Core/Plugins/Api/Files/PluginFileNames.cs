// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:467-490 readNewName (a name the server takes: refused with the reason, never changed behind the user's back; control characters refused as Stalwart 1.0 does; ".", ".." and "/" refused;
// the Files trash's name refused) over lib/file-name-rules.ts fileNameProblem (the server's forbidden characters, reserved names and length: Core FileNameChecks). Stalwart crates/jmap/src/file/ validate_name.
using Barbican.Core.Files;

namespace Barbican.Core.Plugins.Api.Files;

internal static class PluginFileNames
{
    internal const int MaxNameChars = 255;

    /// <summary>The name a plugin gave, trimmed, when it can be a file name at all (no server rules yet); false otherwise.</summary>
    internal static bool TryClean(string raw, out string name)
    {
        name = "";
        if (raw.Length is 0 or > MaxNameChars || raw.Any(c => c is <= '\u001f' or (>= '\u007f' and <= '\u009f'))) return false;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed is "." or ".." || trimmed.Contains('/', StringComparison.Ordinal) || string.Equals(trimmed, FileTrash.TrashName, StringComparison.Ordinal)) return false;
        name = trimmed;
        return true;
    }

    /// <summary>Whether the server's own rules (forbidden characters, reserved names, length in bytes) take the name.</summary>
    internal static bool AcceptedBy(string name, FileNameRules? rules) => FileNameChecks.ProblemOf(name, rules) is null;
}
