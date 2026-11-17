// Mirrors: Rust unified.rs:242 users (the logins the unified inbox reads, the active one first). M2 plan 6.5. Owner unified.
// The account manager's sessions in the app (AccountManagerLogins); a hidden sample run puts two in-process FakeJmap logins here instead.

using Barbican.Core.Jmap;

namespace Barbican.Presentation.Unified;

/// <summary>The logins the unified inbox reads and writes through.</summary>
public interface IUnifiedLogins
{
    /// <summary>The key of the login on screen, or null.</summary>
    string? Active { get; }

    /// <summary>Every remembered login with its client (null while it is not connected).</summary>
    IReadOnlyList<(string Login, IJmapClient? Client)> Logins { get; }

    /// <summary>The client of a login, or null when it is not connected.</summary>
    IJmapClient? ClientOf(string login);

    /// <summary>True when the login's server is a test server (loopback, or the in-process fake of a sample run): automation may write to it.</summary>
    bool IsTestServer(string login);
}
