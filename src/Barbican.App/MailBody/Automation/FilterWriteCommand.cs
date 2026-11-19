#if AUTOMATION_WRITES && WINDOWS
// L-040: prepared hidden command, never registered outside AUTOMATION_WRITES. Execution requires the live write-test guard.
using Barbican.Automation;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Platform.Windows;

namespace Barbican.App.MailBody.Automation;

/// <summary>Runs the synthetic, write-guarded filter verification for the captured active account.</summary>
public sealed class FilterWriteCommand(WriteTestGuard guard, IHookBus hooks, IMailWriterFactory writers, IProfilePaths profile, TimeProvider time) : IAutomationCommand
{
    public string Name => "filter-write-test";

    public Task<string> RunAsync(string args, CancellationToken cancellationToken) => Task.Run(() => RunCoreAsync(args, cancellationToken), CancellationToken.None);

    private async Task<string> RunCoreAsync(string args, CancellationToken ct)
    {
        var preflight = string.Equals(args.Trim(), "preflight", StringComparison.Ordinal);
        if (!preflight && !string.IsNullOrWhiteSpace(args)) return "error usage: filter-write-test [preflight]";
        var (session, refusal) = guard.Check();
        if (session is null || refusal is not null) return "error refused";
        FilterRecoveryJournal? journal = null;
        var enteredRun = false;
        var enteredJournalCreation = false;
        try
        {
            var backend = new FilterVerificationBackend(guard, session, hooks, writers, time);
            if (preflight)
            {
                await backend.CaptureAsync(ct).ConfigureAwait(false);
                return "ok eligible=1 writes=0";
            }
            var secretStore = new WindowsSecretStore(profile.Profile);
            enteredJournalCreation = true;
            journal = await FilterRecoveryJournal.CreateAsync(secretStore, Path.Combine(profile.DataDir, "filter-write-recovery"), ct).ConfigureAwait(false);
            enteredRun = true;
            var result = await FilterWriteRun.RunAsync(backend, journal, ct).ConfigureAwait(false);
            return result.Passed ? "ok passed=1 cleanup=1" : $"error passed=0 cleanup={(result.CleanupComplete ? 1 : 0)}";
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            // Once the runner is entered, retain evidence on any unexpected exception.
            if (enteredRun || (enteredJournalCreation && journal is null)) return "error passed=0 cleanup=0";
            if (journal is not null)
            {
                try { await journal.CompleteAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception cleanup) when (cleanup is not OutOfMemoryException and not StackOverflowException) { return "error passed=0 cleanup=0"; }
            }
            return "error passed=0 cleanup=1";
        }
        finally { journal?.Dispose(); }
    }
}
#endif
