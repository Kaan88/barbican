#if WINDOWS
namespace Barbican.App.Views.Calendar;

/// <summary>Exclusive ownership of a window's native PrintManager event.</summary>
internal sealed class NativePrintLease : IDisposable
{
    private static readonly Lock Gate = new();
    private static readonly HashSet<nint> Owners = [];
    private readonly nint _window;
    private bool _disposed;
    private NativePrintLease(nint window) => _window = window;
    internal static NativePrintLease? TryAcquire(nint window)
    {
        lock (Gate) return Owners.Add(window) ? new NativePrintLease(window) : null;
    }
    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return;
            _disposed = true;
            _ = Owners.Remove(_window);
        }
    }
}
#endif
