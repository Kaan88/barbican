// Mirrors: Rust main.rs:430 restore_session at start (the remembered session signs in without a form); ADR 0008 and M1-plan §6.3 (the agent
// import into a dev or automation profile runs only when BARBICAN_IMPORT_RUST_ACCOUNTS=1 is set, password entries only). Off the UI thread:
// the profile files and the secret store are read on the thread pool.

using Barbican.Core.Accounts;
using Barbican.Core.Storage;
using Barbican.Presentation.Composition;

namespace Barbican.Presentation.Accounts;

/// <summary>Loads the prefs, runs the agent import when asked, then restores the remembered accounts.</summary>
public sealed class AccountsStartup(AccountManager accounts, PrefsStore prefs, ILegacyAccountImport? import = null) : IStartupTask
{
    /// <summary>Set to 1 by an agent to import the Rust app's password accounts into a dev or automation profile (read-only).</summary>
    public const string ImportVariable = "BARBICAN_IMPORT_RUST_ACCOUNTS";

    /// <summary>What the agent import did, when it ran (counts and entry names only).</summary>
    public ImportReport? LastImport { get; private set; }

    /// <summary>The account restored, or null.</summary>
    public AccountInfo? Restored { get; private set; }

    /// <summary>Completes when the start-up work is done (the automation build and tests wait on it).</summary>
#pragma warning disable VSTHRD003 // the task belongs to this object (a TaskCompletionSource it completes); waiting on it cannot deadlock
    public Task Completion => _done.Task;
#pragma warning restore VSTHRD003

    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => Task.Run(() => RunAsync(cancellationToken), cancellationToken);

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await prefs.WaitLoadedAsync(ct).ConfigureAwait(false);
            if (import is not null && string.Equals(Environment.GetEnvironmentVariable(ImportVariable), "1", StringComparison.Ordinal))
            {
                LastImport = await import.ImportAsync(ImportMode.Agent, ct).ConfigureAwait(false);
            }

            Restored = await accounts.RestoreAsync(ct).ConfigureAwait(false);
            _done.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            _done.TrySetCanceled(ct);
        }
        catch (Exception e)
        {
            // a broken store or vault must not leave Completion pending for ever (the automation build and tests wait on it)
            _done.TrySetException(e);
            throw;
        }
    }
}
