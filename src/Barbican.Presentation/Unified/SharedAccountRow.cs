// Mirrors: Bulwark components/layout/sidebar.tsx:2133-2566 (the shared accounts section: one row per shared account, named as the server names it); Rust unified.rs:251 sources. Owner unified (m2-close-a).

namespace Barbican.Presentation.Unified;

/// <summary>One shared account the unified inbox reads.</summary>
/// <param name="Login">The login the account is reached through.</param>
/// <param name="AccountId">The JMAP account id of the shared account.</param>
/// <param name="Name">The name the server gives the account (the id when it gives none).</param>
public sealed record SharedAccountRow(string Login, string AccountId, string Name);
