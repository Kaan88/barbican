// Mirrors: Rust unified.rs:43 parse_row (the (login, shared JMAP account, server id) of a unified row id). Stub from stage F of M2; owner unified.

namespace Barbican.Core.Mail.Unified;

/// <summary>The parts of a unified row id.</summary>
/// <param name="Login">The login (account key) the row was read through.</param>
/// <param name="SharedAccount">The shared JMAP account it was read from, or null for the login's own.</param>
/// <param name="Id">The server id.</param>
public sealed record UnifiedRowId(string Login, string? SharedAccount, string Id);
