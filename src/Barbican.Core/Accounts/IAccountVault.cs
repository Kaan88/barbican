// Mirrors: Rust oauth.rs:373 remember, :393 accounts, :398 forget_account (the remembered accounts in the OS credential store); ADR 0008
// (the C# app's own entries, Barbican.Next/<profile>/accounts). Implemented over ISecretStore in Barbican.Platform (SecretAccountVault);
// Core cannot see the platform layer.

using Barbican.Core.Common;

namespace Barbican.Core.Accounts;

/// <summary>Where the remembered accounts are kept (with their secrets).</summary>
public interface IAccountVault
{
    /// <summary>The remembered accounts, most recently used first (empty when none).</summary>
    Task<Result<IReadOnlyList<StoredAccount>>> LoadAsync(CancellationToken ct);

    /// <summary>Replaces the remembered accounts.</summary>
    Task<Result<Unit>> SaveAsync(IReadOnlyList<StoredAccount> accounts, CancellationToken ct);
}
