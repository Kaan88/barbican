// Mirrors: M1-plan §6.3 ("a fake store for tests"). Also the store of automation profiles (auto-<port>): an automation run never writes
// the OS credential store (ADR 0007, ADR 0008).

using System.Collections.Concurrent;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Platform;

/// <summary>Secrets in memory only.</summary>
public sealed class InMemorySecretStore : IGuardedSecretStore
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
        Write(name, value);
        return Task.FromResult(Result<Unit>.Ok(default));
    }

    public Task<Result<Unit>> WriteWhileCurrentAsync(SecretName name, string value, Func<Func<bool>, bool> publishWhileCurrent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(publishWhileCurrent);
        var accepted = publishWhileCurrent(() =>
        {
            if (ct.IsCancellationRequested) return false;
            Write(name, value);
            return true;
        });
        return Task.FromResult(accepted ? Result<Unit>.Ok(default) :
            Result<Unit>.Fail(new ValidationError("Secret write approval is no longer current.")));
    }

    private void Write(SecretName name, string value)
    {
        _values[name] = value;
        Interlocked.Increment(ref _writes);
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> DeleteAsync(SecretName name, CancellationToken ct)
    {
        _values.TryRemove(name, out _);
        return Task.FromResult(Result<Unit>.Ok(default));
    }
}
