// Mirrors: Rust palette.rs rebuild :269 (a header row per group, then its commands; a row that cannot run is dimmed). Owner menus.

namespace Barbican.Presentation.Palette;

/// <summary>One line of the open palette: a group heading or a command.</summary>
/// <param name="Header">The group heading (a command row has none).</param>
/// <param name="Command">The command (a heading row has none).</param>
/// <param name="Enabled">The command has a handler and can run now.</param>
public sealed record PaletteRow(string Header, PaletteCommand? Command, bool Enabled)
{
    public bool IsHeader => Command is null;
}
