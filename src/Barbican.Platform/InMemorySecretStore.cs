// Mirrors: M1-plan §6.3 ("a fake store for tests"). Also the store of automation profiles (auto-<port>): an automation run never writes
// the OS credential store (ADR 0007, ADR 0008).

using System.Collections.Concurrent;
using Barbican.Core.Common;

namespace Barbican.Platform;

/// <summary>Secrets in memory only.</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<SecretName, string> _values = new();

    /// <summary>How many writes happened (tests).</summary>
    public int Writes => Volatile.Read(ref _writes);

    private int _writes;

    /// <summary>The names held.</summary>
    public IReadOnlyCollection<SecretName> Names => [.. _values.Keys];

    /// <inheritdoc/>
    public Task<Result<string?>> ReadAsync(SecretName name, CancellationToken ct) =>
        Task.FromResult(Result<string?>.Ok(_values.TryGetValue(name, out var v) ? v : null));

    /// <inheritdoc/>
    public Task<Result<Unit>> WriteAsync(SecretName name, string value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(value);
        _values[name] = value;
        Interlocked.Increment(ref _writes);
        return Task.FromResult(Result<Unit>.Ok(default));
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> DeleteAsync(SecretName name, CancellationToken ct)
    {
        _values.TryRemove(name, out _);
        return Task.FromResult(Result<Unit>.Ok(default));
    }
}
