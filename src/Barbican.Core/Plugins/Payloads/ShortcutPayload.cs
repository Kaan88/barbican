// Mirrors: Bulwark onBeforeShortcut / onAfterShortcut (lib/plugin-hook-catalog.ts; hooks/use-keyboard-shortcuts.ts). Owner mailcore.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A key and the action it resolved to.</summary>
public sealed record ShortcutPayload(string Key, string Action);
