// Mirrors: Rust palette.rs:47 Cmd; Bulwark components/command-palette/ (the row model). Stub from stage F of M2; owner menus.

namespace Barbican.Presentation.Palette;

/// <summary>One row of the command palette.</summary>
/// <param name="Id">The id the row runs: a CommandIds constant, or "go:files", "mail:read", "settings:appearance" style ids Rust used.</param>
/// <param name="Group">The section heading ("Calendar", "Mail", "Go to", "Actions", "Account", "Settings").</param>
/// <param name="Icon">The line-icon key (Rust holds the SVG path; the C# icon set names it).</param>
/// <param name="Label">The visible text.</param>
/// <param name="Keywords">Extra words the filter matches.</param>
/// <param name="Shortcut">The key hint, empty when there is none (or when single-key shortcuts are off).</param>
public sealed record PaletteCommand(string Id, string Group, string Icon, string Label, IReadOnlyList<string> Keywords, string Shortcut);
