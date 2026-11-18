// Mirrors: Rust jmap.rs mailbox writes; Bulwark lib/jmap/client.ts:4642-4732 (Mailbox/set create, rename, move, subscribe, destroy, role), :8383-8395 (the Inbox metadata update with ifInState), components/settings/folder-settings.tsx, organize-settings.tsx, stores/folder-prefs; RFC 8621 section 2; Stalwart crates/jmap/src/mailbox/set.rs (role uniqueness, onDestroyRemoveEmails). G-036..G-041, I-428..I-430. Contract from the M6 plan section 4.8; owner srv-data.

namespace Barbican.Core.Settings.Folders;

/// <summary>The per-folder settings of an account, by mailbox id.</summary>
public sealed record FolderAdminPrefs(IReadOnlyDictionary<string, string> Icons, IReadOnlyDictionary<string, string> CountModes, IReadOnlySet<string> Pinned, IReadOnlySet<string> Muted);
