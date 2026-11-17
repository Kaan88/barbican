// A refresh-token store in memory, standing in for the account vault (another process's rotation is a write to it).

using Barbican.Core.Auth;

namespace Barbican.Core.Tests.Auth;

internal sealed class MemoryRefreshStore : IRefreshTokenStore
{
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);

    public int Writes { get; private set; }

    public void Set(string key, string refresh)
    {
        lock (_tokens)
        {
            _tokens[key] = refresh;
        }
    }

    public string? Get(string key)
    {
        lock (_tokens)
        {
            return _tokens.GetValueOrDefault(key);
        }
    }

    public Task<string?> ReadAsync(string accountKey, CancellationToken ct) => Task.FromResult(Get(accountKey));

    public Task WriteAsync(string accountKey, string refresh, CancellationToken ct)
    {
        lock (_tokens)
        {
            Writes++;
            _tokens[accountKey] = refresh;
        }

        return Task.CompletedTask;
    }
}
