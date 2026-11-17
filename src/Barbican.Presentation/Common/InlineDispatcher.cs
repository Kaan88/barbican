// Mirrors: M1-plan §4.3 (IDispatcher). A dispatcher that runs work where it is called: tests and headless runs (no UI thread to marshal to). Owner mailcore.

namespace Barbican.Presentation.Common;

/// <summary>Runs every action at once on the calling thread.</summary>
public sealed class InlineDispatcher : IDispatcher
{
    public bool HasThreadAccess => true;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }

    public Task InvokeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action();
    }
}
