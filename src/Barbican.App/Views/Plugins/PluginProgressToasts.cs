#if WINDOWS
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Notifications;
using Barbican.Core.Plugins.Runtime;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Common;

namespace Barbican.App.Views.Plugins;

internal sealed class PluginProgressToasts : IDisposable
{
    private const int MaxPerAddOn = 5;
    private const int MaxInWindow = 32;
    private readonly ToastService _toasts;
    private readonly IDispatcher _dispatcher;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _active = new(StringComparer.Ordinal);
    private bool _disposed;

    public PluginProgressToasts(ToastService toasts, IDispatcher dispatcher)
    {
        _toasts = toasts;
        _dispatcher = dispatcher;
        _toasts.Dismissed += OnDismissed;
    }

    // Both plugin entry points run on the UI thread under the exact live frame and package grant.
    internal PluginToastProgressResult Show(ToastRequest request, IPluginInvocationContext context, Func<bool> takeBudget)
    {
        lock (_gate)
        {
            if (_disposed) return new(null, PluginErrorCode.Unavailable);
            var owner = context.Binding;
            if (_active.Count >= MaxInWindow || _active.Values.Count(entry => SameAddOn(entry.Owner, owner)) >= MaxPerAddOn || !takeBudget())
                return new(null, PluginErrorCode.RateLimited);

            var id = Guid.NewGuid().ToString("N");
            context.MarkMutationIssued();
            try
            {
                _ = _toasts.ShowPrepared(request with { IsProgress = true }, handle =>
                {
                    var lifetime = owner.Lifetime.Register(() => _dispatcher.Post(() => Release(id)));
                    _active.Add(id, new(owner, handle, lifetime));
                });
            }
            catch
            {
                Release(id);
                throw;
            }

            return new(id, null);
        }
    }

    internal void Dismiss(string id, IPluginInvocationContext context)
    {
        Entry entry;
        lock (_gate)
        {
            if (!_active.TryGetValue(id, out entry) || !ReferenceEquals(entry.Owner, context.Binding)) return;
            context.MarkMutationIssued();
            _ = _active.Remove(id);
        }

        Close(entry);
    }

    private void OnDismissed(object? sender, ToastDismissedEventArgs args)
    {
        if (!args.Toast.IsProgress) return;
        lock (_gate)
        {
            var id = _active.FirstOrDefault(item => item.Value.Handle == args.Toast.Handle).Key;
            if (id is not null && _active.Remove(id, out var entry)) _ = entry.Lifetime.Unregister();
        }
    }

    private void Release(string id)
    {
        Entry entry;
        lock (_gate)
        {
            if (!_active.Remove(id, out entry)) return;
        }

        Close(entry);
    }

    private void Close(Entry entry)
    {
        _ = entry.Lifetime.Unregister();
        _toasts.Dismiss(entry.Handle);
    }

    public void Dispose()
    {
        Entry[] entries;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            entries = [.. _active.Values];
            _active.Clear();
        }

        _toasts.Dismissed -= OnDismissed;
        foreach (var entry in entries) Close(entry);
    }

    private static bool SameAddOn(PluginRuntimeBinding left, PluginRuntimeBinding right) =>
        string.Equals(left.Profile, right.Profile, StringComparison.Ordinal) &&
        string.Equals(left.Package.Id, right.Package.Id, StringComparison.Ordinal);

    private readonly record struct Entry(PluginRuntimeBinding Owner, ToastHandle Handle, CancellationTokenRegistration Lifetime);
}
#endif
