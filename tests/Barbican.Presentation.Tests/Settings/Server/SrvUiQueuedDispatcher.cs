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

    public Task InvokeAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                var task = action();
                _ = task.ContinueWith(t =>
                {
                    if (t.IsFaulted) { completion.SetException(t.Exception!.InnerExceptions); }
                    else if (t.IsCanceled) { completion.SetCanceled(); }
                    else { completion.SetResult(); }
                }, TaskScheduler.Default);
            }
            catch (Exception e)
            {
                completion.SetException(e);
            }
        });
        return completion.Task;
    }

    /// <summary>Awaits the task while running queued work on the calling (test) thread, so UI-thread callbacks never overlap the test's own edits, as on the real UI thread.</summary>
#pragma warning disable VSTHRD003 // the caller started the task; this only pumps while it runs
    public async Task RunAsync(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        while (!task.IsCompleted)
        {
            Pump();
            _ = await Task.WhenAny(task, Task.Delay(1)).ConfigureAwait(false);
        }

        await task.ConfigureAwait(false);
    }

    /// <summary>As <see cref="RunAsync(Task)"/>, returning the task's result.</summary>
    public async Task<T> RunAsync<T>(Task<T> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        while (!task.IsCompleted)
        {
            Pump();
            _ = await Task.WhenAny(task, Task.Delay(1)).ConfigureAwait(false);
        }

        return await task.ConfigureAwait(false);
    }
#pragma warning restore VSTHRD003

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
