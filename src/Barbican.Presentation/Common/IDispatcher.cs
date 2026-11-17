// Mirrors: M1-plan §4.3 (UI-thread marshalling; the App head implements it over DispatcherQueue). Contract stub from F2; owner mailcore.

namespace Barbican.Presentation.Common;

/// <summary>Runs work on the UI thread.</summary>
public interface IDispatcher
{
    bool HasThreadAccess { get; }

    void Post(Action action);

    Task InvokeAsync(Func<Task> action);
}
