// M1 hotfix (sign-in): a stand-in for the WinUI UI thread. One dedicated thread pumps a queue under its own SynchronizationContext (as the
// DispatcherQueue does), so an await started there comes back there, and it is the IDispatcher of the ViewModels under test. An exception
// thrown by a posted action is collected (the app's Application.UnhandledException) instead of ending the test process.

using System.Collections.Concurrent;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Tests.Support;

internal sealed class UiThread : IDispatcher, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
    private readonly Thread _thread;
    private readonly List<Exception> _unhandled = [];

    public UiThread()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "test-ui" };
        _thread.Start();
    }

    public bool HasThreadAccess => Thread.CurrentThread == _thread;

    /// <summary>Exceptions that escaped a posted action.</summary>
    public IReadOnlyList<Exception> Unhandled
    {
        get
        {
            lock (_unhandled)
            {
                return [.. _unhandled];
            }
        }
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _queue.Add((_ => action(), null));
    }

    public Task InvokeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            Task t;
            try
            {
                t = action();
            }
#pragma warning disable CA1031 // handed to the awaiting test
            catch (Exception e)
#pragma warning restore CA1031
            {
                done.SetException(e);
                return;
            }

            _ = t.ContinueWith(
                x =>
                {
                    if (x.IsFaulted)
                    {
                        done.SetException(x.Exception!.InnerExceptions);
                    }
                    else if (x.IsCanceled)
                    {
                        done.SetCanceled();
                    }
                    else
                    {
                        done.SetResult();
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        });
        return done.Task;
    }

    /// <summary>Waits until everything posted so far (and what that posted in turn, a few rounds deep) has run.</summary>
    public async Task FlushAsync()
    {
        for (var i = 0; i < 5; i++)
        {
            await InvokeAsync(() => Task.CompletedTask);
        }
    }

    public void Dispose() => _queue.CompleteAdding();

    private void Run()
    {
        SynchronizationContext.SetSynchronizationContext(new Context(this));
        foreach (var (callback, state) in _queue.GetConsumingEnumerable())
        {
            try
            {
                callback(state);
            }
#pragma warning disable CA1031 // the UI thread's last-chance handler: collected for the test
            catch (Exception e)
#pragma warning restore CA1031
            {
                lock (_unhandled)
                {
                    _unhandled.Add(e);
                }
            }
        }
    }

    private sealed class Context(UiThread owner) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            if (!owner._queue.IsAddingCompleted)
            {
                owner._queue.Add((d, state));
            }
        }

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("No Send on the UI stand-in.");

        public override SynchronizationContext CreateCopy() => this;
    }
}
