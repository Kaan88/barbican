// Mirrors: M6 plan section 3.5 (D-M6-00). What every Sample* fake does to look like a real service: work completes on the thread pool (the caller's continuation does not resume on its own context), events are raised from a pool thread.
// Frozen in the M6 plan commit; owner integrator.

namespace Barbican.Presentation.Settings.Sample;

/// <summary>The thread behaviour of the real settings services, for fakes.</summary>
public static class SampleThreading
{
    /// <summary>Runs the work on the pool and completes there (never inline, never on the caller's synchronisation context).</summary>
    public static Task<T> OffThreadAsync<T>(Func<T> work) => Task.Run(work);

    /// <summary>Runs the work on the pool.</summary>
    public static Task OffThreadAsync(Action work) => Task.Run(work);

    /// <summary>Raises an event from a pool thread, after the call that caused it returned (a real service raises when its write finished).</summary>
    public static void RaiseOnPool(Action raise)
    {
        ArgumentNullException.ThrowIfNull(raise);
        _ = Task.Run(raise);
    }
}
