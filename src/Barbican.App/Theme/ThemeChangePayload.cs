// Mirrors: Bulwark stores/theme-store.ts:703-705 (onThemeChange emits { theme: state.theme, resolved: state.resolvedTheme }); lib/plugin-hook-catalog.ts:299.

namespace Barbican.App.Theme;

/// <summary>Payload of the onThemeChange hook.</summary>
/// <param name="Theme">The setting: "light", "dark" or "system".</param>
/// <param name="Resolved">What it resolved to: "light" or "dark".</param>
public sealed record ThemeChangePayload(string Theme, string Resolved);
