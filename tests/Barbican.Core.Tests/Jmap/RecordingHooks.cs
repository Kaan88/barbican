// An IHookBus that records what the sync engine tells plugins. Owner jmap.

using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Tests.Jmap;

internal sealed class RecordingHooks : IHookBus
{
    private readonly List<(string Hook, object? Payload)> _seen = [];

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

    public bool HasHandlers(string hook) => true;

    public Func<string, CancellationToken, ValueTask>? OnNotification { get; set; }

    public ValueTask NotifyAsync<TPayload>(string hook, TPayload payload, CancellationToken ct)
    {
        lock (_seen)
        {
            _seen.Add((hook, payload));
        }

        return OnNotification?.Invoke(hook, ct) ?? ValueTask.CompletedTask;
    }

    public ValueTask<bool> InterceptAsync<TPayload>(string hook, TPayload payload, CancellationToken ct) => ValueTask.FromResult(true);

    public ValueTask<T> TransformAsync<T>(string hook, T value, CancellationToken ct) => ValueTask.FromResult(value);
}
