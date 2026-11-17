// Mirrors: Rust oauth.rs:299 stored_refresh, :305 store_rotated (the refresh token in the remembered accounts, which another instance may
// have rotated). spec B-OAUTH-015. Implemented by the account store (Core/Accounts/AccountStore.cs).

namespace Barbican.Core.Auth;

/// <summary>The refresh token remembered for an account.</summary>
public interface IRefreshTokenStore
{
    /// <summary>The stored refresh token of <paramref name="accountKey"/>, or null.</summary>
    Task<string?> ReadAsync(string accountKey, CancellationToken ct);

    /// <summary>Stores a rotated refresh token (only when the account is remembered; never creates or deletes one).</summary>
    Task WriteAsync(string accountKey, string refresh, CancellationToken ct);
}
