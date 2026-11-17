// Mirrors: Rust oauth.rs:314-:326 (keyring entries by account). Contract stub from F2; owner accounts.

namespace Barbican.Platform;

/// <summary>The name of a stored secret: what it is for, and the account it belongs to (if any).</summary>
public readonly record struct SecretName(string Kind, string? Account = null);
