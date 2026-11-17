// Mirrors: Rust main.rs sign-in (password plus TOTP); spec I-098..I-104. Contract stub from F2; owner accounts.

namespace Barbican.Core.Accounts;

/// <summary>A password sign-in request (the password is never stored or logged).</summary>
public sealed record PasswordSignIn(string Server, string User, string Password, string? TotpCode, bool Remember);
