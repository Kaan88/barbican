// A test dispatcher that is never on the UI thread: Post queues, Pump runs. Proves the view models post to the UI thread before they raise (D-M6-00). M6 stream srv-ui.

using Barbican.Presentation.Common;

namespace Barbican.Presentation.Tests.Settings.Server;

internal sealed class SrvUiQueuedDispatcher : IDispatcher
{
    private readonly Queue<Action> _queue = new();

    public bool HasThreadAccess => false;

    public int Pending
    {
        get
        {
            lock (_queue)
            {
                return _queue.Count;
            }
        }
    }

    public void Post(Action action)
    {
        lock (_queue)
        {
            _queue.Enqueue(action);
        }
    }

    public Task InvokeAsync(Func<Task> action) => action();

    public void Pump()
    {
        while (true)
        {
            Action next;
            lock (_queue)
            {
                if (_queue.Count == 0)
                {
                    return;
                }

                next = _queue.Dequeue();
            }

            next();
        }
    }
}
