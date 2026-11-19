#if AUTOMATION_WRITES && WINDOWS
// L-040: prepared hidden command, never registered outside AUTOMATION_WRITES. The caller must approve execution separately.
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
        if (!string.IsNullOrWhiteSpace(args)) return "error filter-write-test takes no arguments";
        var (session, refusal) = guard.Check();
        if (session is null || refusal is not null) return "error refused";
        FilterRecoveryJournal? journal = null;
        try
        {
            var backend = new FilterVerificationBackend(guard, session, hooks, writers, time);
            var secretStore = new WindowsSecretStore(profile.Profile);
            journal = await FilterRecoveryJournal.CreateAsync(secretStore, Path.Combine(profile.DataDir, "filter-write-recovery"), ct).ConfigureAwait(false);
            var result = await FilterWriteRun.RunAsync(backend, journal, ct).ConfigureAwait(false);
            return result.Passed ? "ok passed=1 cleanup=1" : $"error passed=0 cleanup={(result.CleanupComplete ? 1 : 0)}";
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            // RunAsync only throws before a server mutation. Any later failure retains the journal in its returned outcome.
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
