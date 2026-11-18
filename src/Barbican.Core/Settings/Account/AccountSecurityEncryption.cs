// Mirrors: Bulwark account-security-store.ts fetchCryptoInfo (x:AccountSettings encryptionAtRest), account-security-settings.tsx (the Encryption section); Stalwart crates/registry/src/schema/structs.rs (EncryptionAtRest). G-030; hook onEncryptionChange. Contract from the M6 plan section 4.5; owner set-data.

namespace Barbican.Core.Settings.Account;

/// <summary>The encryption-at-rest setting of the account: whether it is on and the method the server reports (empty when off).</summary>
public sealed record AccountSecurityEncryption(bool Enabled, string Method);
