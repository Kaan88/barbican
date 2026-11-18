// Mirrors: Rust settings.rs:160 export, :167 import, :178 reset; Bulwark settings export (components/settings/about-data-settings.tsx, lib/settings-export.ts shape: a flat object or one wrapped in `settings`); B-SET-006, G-064 (settings sync stays unbuilt). Contract from the M6 plan section 4.3; owner set-data.

namespace Barbican.Core.Settings.Backup;

/// <summary>What an import would do, shown in its confirmation: whether it is a Bulwark or a Barbican file, how many keys of the schema it carries, which keys the schema does not know (kept, not dropped).</summary>
public sealed record SettingBackupPlan(bool IsBulwark, bool HasDesktop, int KnownKeys, IReadOnlyList<string> UnknownKeys);
