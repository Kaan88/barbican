using Barbican.Core.Plugins.Runtime;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>Shares startup, teardown and retry state with the revocation coordinator.</summary>
internal sealed class PluginSandboxStartupLifetime(PluginSandboxSession session) : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private IDisposable? _registration;
    private IPluginRuntimeInstance? _instance;
    private IDisposable? _observers;
    private IDisposable? _oauthReceiver;
    private TaskCompletionSource? _activation;
    private TaskCompletionSource? _closeAttempt;
    private bool _closing;
    private bool _sessionClosed;
    private bool _finished;

    public void AttachRegistration(IDisposable registration)
    {
        lock (_gate)
        {
            if (!_finished)
            {
                _registration = registration;
                return;
            }
        }
        registration.Dispose();
    }

    public bool TryBeginActivation()
    {
        lock (_gate)
        {
            if (_closing || !session.Binding.IsAlive) return false;

            _activation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }
    }

    public bool FinishActivation(IPluginRuntimeInstance? instance, IDisposable? observers, IDisposable? oauthReceiver)
    {
        lock (_gate)
        {
            _instance = instance;
            _observers = observers;
            _oauthReceiver = oauthReceiver;
            _activation!.SetResult();
            return !_closing && instance is not null && session.Binding.IsAlive
                && ReferenceEquals(session.Binding, instance.Binding);
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource attempt;
        Task activationFinished;
        lock (_gate)
        {
            if (_closeAttempt is { } prior) return new(prior.Task);
            _closing = true;
            _closeAttempt = attempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
            activationFinished = _activation?.Task ?? Task.CompletedTask;
        }

        _ = CloseAsync(activationFinished, attempt);
        return new(attempt.Task);
    }

#pragma warning disable VSTHRD003 // The activation signal completes asynchronously after cancellation, without a UI-context dependency.
    private async Task CloseAsync(Task activationFinished, TaskCompletionSource attempt)
    {
        try
        {
            await CloseResourcesAsync(activationFinished).ConfigureAwait(false);
            IDisposable? registration;
            lock (_gate)
            {
                _finished = true;
                registration = _registration;
                _registration = null;
            }
            registration?.Dispose();
            attempt.SetResult();
        }
#pragma warning disable CA1031 // Complete the shared attempt and permit the coordinator to retry.
        catch (Exception error)
#pragma warning restore CA1031
        {
            lock (_gate)
            {
                _closeAttempt = null;
                attempt.SetException(error);
            }
        }
    }

    private async Task CloseResourcesAsync(Task activationFinished)
    {
        try
        {
            if (!_sessionClosed)
            {
                await session.DisposeAsync().ConfigureAwait(false);
                _sessionClosed = true;
            }
        }
        finally
        {
            // Closing the session cancels activation. Wait for it to hand back any late instance.
            await activationFinished.ConfigureAwait(false);
            _observers?.Dispose();
            _observers = null;
            _oauthReceiver?.Dispose();
            _oauthReceiver = null;
            if (_instance is { } instance)
            {
                await instance.DisposeAsync().ConfigureAwait(false);
                _instance = null;
            }
        }
    }
#pragma warning restore VSTHRD003
}
