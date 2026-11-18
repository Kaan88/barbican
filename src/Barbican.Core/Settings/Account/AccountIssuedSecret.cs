// Mirrors: Bulwark stores/account-security-store.ts (x:AccountPassword/get and set at :320-401, x:AppPassword and x:ApiKey query, get and set at :220-403), lib/stalwart/principal.ts:34-59 (x:AccountSettings, the account's own settings singleton), components/settings/account-security-settings.tsx, masked-addresses-section.tsx; Stalwart crates/jmap/src/registry/mapping/ (account_password, app_password, api_key, masked_email), crates/registry/src/schema/structs.rs. G-026..G-030, I-403. Hooks onPasswordChange, onTotpChange, onAppPasswordChange, onEncryptionChange, onDisplayNameChange. Contract from the M6 plan section 4.5; owner set-data.

namespace Barbican.Core.Settings.Account;

/// <summary>A newly created app password or API key: the secret is shown once, never stored or logged.</summary>
public sealed record AccountIssuedSecret(string Id, string Secret);
