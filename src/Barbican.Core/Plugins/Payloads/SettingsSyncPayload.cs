// Mirrors: Bulwark lib/plugin-hook-catalog.ts:240 { direction } (onSettingsSync); D-M6-A04: Barbican has no settings sync, the hook fires when settings are replaced from a file. Owner set-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>Settings were replaced from outside: <see cref="Direction"/> is "pulled", <see cref="Source"/> says from where ("import").</summary>
public sealed record SettingsSyncPayload(string Direction, string Source);
