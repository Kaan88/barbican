// A hook bus for the files tests: it records every observer call and transform, and refuses the intercepts that were named. Owner files-data.

using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Tests.Files;

internal sealed class FileHooks : IHookBus
{
    private readonly List<(string Hook, object? Payload)> _seen = [];
    private readonly HashSet<string> _deny = new(StringComparer.Ordinal);

    public IReadOnlyList<(string Hook, object? Payload)> Seen
    {
        get
        {
            lock (_seen)
            {
                return [.. _seen];
            }
        }
    }

    /// <summary>Makes the interceptors of a hook answer false.</summary>
    public void Deny(string hook) => _deny.Add(hook);

    public bool HasHandlers(string hook) => true;

    public ValueTask NotifyAsync<TPayload>(string hook, TPayload payload, CancellationToken ct)
    {
        Record(hook, payload);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> InterceptAsync<TPayload>(string hook, TPayload payload, CancellationToken ct)
    {
        Record(hook, payload);
        return ValueTask.FromResult(!_deny.Contains(hook));
    }

    public ValueTask<T> TransformAsync<T>(string hook, T value, CancellationToken ct) => ValueTask.FromResult(value);

    private void Record(string hook, object? payload)
    {
        lock (_seen)
        {
            _seen.Add((hook, payload));
        }
    }
}
