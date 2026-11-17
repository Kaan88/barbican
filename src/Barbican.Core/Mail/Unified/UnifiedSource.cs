// Mirrors: Rust unified.rs:213 struct Source (a login, its client, the JMAP account read and whether it is a shared one), :226 source_key.
// Owner unified.

using Barbican.Core.Jmap;

namespace Barbican.Core.Mail.Unified;

/// <summary>One account read by the unified inbox: a login's own account, or a shared account the login reaches.</summary>
/// <param name="Login">The account key of the login the account is read through.</param>
/// <param name="Client">The login's client.</param>
/// <param name="AccountId">The JMAP account read.</param>
/// <param name="Shared">True for a shared account (a group), false for the login's own.</param>
public sealed record UnifiedSource(string Login, IJmapClient Client, string AccountId, bool Shared)
{
    /// <summary>The shared account id, or null for the login's own (the middle part of a row id).</summary>
    public string? SharedAccount => Shared ? AccountId : null;

    /// <summary>The source key (Rust source_key: the login, plus <c>#account</c> for a shared one).</summary>
    public string Key => UnifiedRows.SourceKey(Login, SharedAccount);
}
