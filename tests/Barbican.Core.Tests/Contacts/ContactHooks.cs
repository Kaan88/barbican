// A hook bus for the contact tests: records every hook, can stop an intercept and can reshape a transform.

using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Tests.Contacts;

internal sealed class ContactHooks : IHookBus
{
    private readonly List<(string Hook, object? Payload)> _seen = [];
    private readonly HashSet<string> _stopped = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<object, object>> _transforms = new(StringComparer.Ordinal);

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

    public IReadOnlyList<string> Names => [.. Seen.Select(s => s.Hook)];

    /// <summary>The intercept hook says no.</summary>
    public void Stop(string hook) => _ = _stopped.Add(hook);

    public void Transform<T>(string hook, Func<T, T> change)
        where T : class => _transforms[hook] = v => change((T)v);

    public bool HasHandlers(string hook) => true;

    public ValueTask NotifyAsync<TPayload>(string hook, TPayload payload, CancellationToken ct)
    {
        lock (_seen)
        {
            _seen.Add((hook, payload));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> InterceptAsync<TPayload>(string hook, TPayload payload, CancellationToken ct)
    {
        lock (_seen)
        {
            _seen.Add((hook, payload));
        }

        return ValueTask.FromResult(!_stopped.Contains(hook));
    }

    public ValueTask<T> TransformAsync<T>(string hook, T value, CancellationToken ct)
    {
        lock (_seen)
        {
            _seen.Add((hook, value));
        }

        return ValueTask.FromResult(_transforms.TryGetValue(hook, out var change) && value is not null ? (T)change(value) : value);
    }
}
