// Mirrors: M1-plan §4.3 (IDispatcher: the App head implements it over DispatcherQueue).

using Barbican.Presentation.Common;
using Microsoft.UI.Dispatching;

namespace Barbican.App.Shell;

/// <summary>Runs work on the UI thread through the window's <see cref="DispatcherQueue"/>.</summary>
public sealed class DispatcherQueueDispatcher(DispatcherQueue queue) : IDispatcher
{
    public bool HasThreadAccess => queue.HasThreadAccess;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        // After the window closed the queue is shut down; a late sync result is dropped (throwing here would crash the process from a thread-pool continuation).
        _ = queue.TryEnqueue(() => action());
    }

    public Task InvokeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (queue.HasThreadAccess)
        {
            return action();
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable MA0147, VSTHRD101 // the body catches every failure and hands it to the awaiting caller
        if (!queue.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    done.SetResult();
                }
#pragma warning disable CA1031 // the failure belongs to the caller awaiting the returned task
                catch (Exception e)
#pragma warning restore CA1031
                {
                    done.SetException(e);
                }
            }))
        {
            done.SetException(new InvalidOperationException("The UI thread's queue is shut down."));
        }
#pragma warning restore MA0147, VSTHRD101

        return done.Task;
    }
}
