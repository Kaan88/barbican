// Mirrors: Bulwark lib/plugin-hook-catalog.ts:237-238 { keys: string[] } (onSettingsExport, onSettingsImport). Owner set-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The keys of the settings that were exported or imported (never their values: a value may be a secret of the user's plug-ins).</summary>
public sealed record SettingsKeysPayload(IReadOnlyList<string> Keys);
