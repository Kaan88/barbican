namespace Barbican.Presentation.Composition;

/// <summary>
/// Work a feature starts once the main window exists (docs/milestones/M1-plan.md §3.3: a
/// multi-registration; each stream adds its own in its registration file). The App runs every
/// registered task on the UI thread after the window is shown (or placed off-screen), without
/// awaiting one before the next starts; a task that needs the thread pool moves there itself.
/// </summary>
public interface IStartupTask
{
    Task StartAsync(CancellationToken cancellationToken);
}
