// Mirrors: Rust palette.rs:61 command_matches, :92 commands, :178 filter; Bulwark lib/command-registry.ts:53-96 commandMatches and the command-palette
// ordering (the open app's own, Go to, Actions, Account). Stub from stage F of M2; owner menus.

using Barbican.Core.Common;

namespace Barbican.Presentation.Palette;

/// <summary>The palette's pure model: which commands exist for a context and which match what was typed.</summary>
public static class PaletteModel
{
    /// <summary>Every word of the needle is in the label or the keywords (case-insensitive); an empty needle matches everything.</summary>
    public static bool CommandMatches(string needle, string label, IReadOnlyList<string> keywords)
    {
        throw new PortPendingException("palette.rs:61 command_matches");
    }

    /// <summary>The commands for a context in Bulwark's order.</summary>
    public static IReadOnlyList<PaletteCommand> Commands(PaletteContext context)
    {
        throw new PortPendingException("palette.rs:92 commands");
    }

    /// <summary>The commands whose label or keywords match the needle, in order.</summary>
    public static IReadOnlyList<PaletteCommand> Filter(IReadOnlyList<PaletteCommand> commands, string needle)
    {
        throw new PortPendingException("palette.rs:178 filter");
    }
}
