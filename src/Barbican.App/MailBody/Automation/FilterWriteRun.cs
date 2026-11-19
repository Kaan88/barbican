#if AUTOMATION_WRITES
// L-040: synthetic Inbox filter round trip. The backend is bound to one captured client; all writes require its live guard.
using System.Security.Cryptography;
using System.Text.Json;

namespace Barbican.App.MailBody.Automation;

/// <summary>Orchestrates write-ahead recovery and never applies a plan that includes other mail.</summary>
public static class FilterWriteRun
{
    /// <summary>Runs one synthetic round trip, preserving the recovery journal on any ambiguity.</summary>
    public static async Task<FilterWriteOutcome> RunAsync(IFilterWriteBackend backend, FilterRecoveryJournal journal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(journal);
        var snapshot = await backend.CaptureAsync(ct).ConfigureAwait(false);
        var record = new FilterWriteRecord(snapshot, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12)));
        await backend.EnsureUniqueAsync(record.Token, ct).ConfigureAwait(false);
        await SaveAsync(journal, record, ct).ConfigureAwait(false);
        var passed = false;
        try
        {
            await PerformAsync(backend, journal, record, ct).ConfigureAwait(false);
            passed = true;
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            // The journal's pending state is deliberately retained; the request may have reached the server.
        }

        var cleaned = await CleanupAsync(backend, journal, record).ConfigureAwait(false);
        return new FilterWriteOutcome(passed && cleaned, cleaned);
    }

    private static async Task PerformAsync(IFilterWriteBackend backend, FilterRecoveryJournal journal, FilterWriteRecord record, CancellationToken ct)
    {
        record.ImportPending = true;
        await SaveAsync(journal, record, ct).ConfigureAwait(false);
        record.EmailId = await backend.ImportAsync(record.Token, ct).ConfigureAwait(false);
        record.ImportPending = false;
        await SaveAsync(journal, record, ct).ConfigureAwait(false);
        record.FolderPending = true;
        await SaveAsync(journal, record, ct).ConfigureAwait(false);
        record.FolderId = await backend.CreateFolderAsync(record.Token, ct).ConfigureAwait(false);
        record.FolderPending = false;
        await SaveAsync(journal, record, ct).ConfigureAwait(false);
        record.ScriptPending = true;
        await SaveAsync(journal, record, ct).ConfigureAwait(false);
        var saved = await backend.SaveRuleAsync(record.Original, record.Token, record.FolderId, ct).ConfigureAwait(false);
        record.SavedState = saved.State;
        record.ScriptPending = false;
        await SaveAsync(journal, record, ct).ConfigureAwait(false);
        var scan = await backend.ScanAsync(saved.Rule, ct).ConfigureAwait(false);
        if (!FilterWriteSafety.IsExactPlan(scan, record.EmailId, record.FolderId)) throw new InvalidOperationException("The scan did not match the synthetic message exactly.");
        record.ApplyPending = true;
        await SaveAsync(journal, record, ct).ConfigureAwait(false);
        await backend.ApplyAndVerifyAsync(scan, record.EmailId, record.FolderId, ct).ConfigureAwait(false);
        record.ApplyPending = false;
        await SaveAsync(journal, record, ct).ConfigureAwait(false);
    }

    private static async Task<bool> CleanupAsync(IFilterWriteBackend backend, FilterRecoveryJournal journal, FilterWriteRecord record)
    {
        try
        {
            var ct = CancellationToken.None;
            // An unacknowledged script save could have installed a rule referencing these artifacts.
            if (record.ScriptPending) return false;
            if (record.ImportPending && await backend.DiscoverOwnMessageAsync(record.Token, ct).ConfigureAwait(false) is { } foundMessage)
            {
                record.EmailId = foundMessage;
                record.ImportPending = false;
                await SaveAsync(journal, record, ct).ConfigureAwait(false);
            }
            if (record.FolderPending && await backend.DiscoverOwnFolderAsync(record.Token, ct).ConfigureAwait(false) is { } foundFolder)
            {
                record.FolderId = foundFolder;
                record.FolderPending = false;
                await SaveAsync(journal, record, ct).ConfigureAwait(false);
            }
            if (record.SavedState is not null && !record.ScriptPending)
            {
                if (!await backend.RestoreAsync(record.Original, record.SavedState, ct).ConfigureAwait(false)) return false;
                record.SavedState = null;
                await SaveAsync(journal, record, ct).ConfigureAwait(false);
            }
            if (record.EmailId is not null && !record.ImportPending)
            {
                if (!await backend.DestroyOwnMessageAsync(record.Token, record.EmailId, ct).ConfigureAwait(false)) return false;
                record.EmailId = null;
                record.ApplyPending = false;
                await SaveAsync(journal, record, ct).ConfigureAwait(false);
            }
            if (record.FolderId is not null && !record.FolderPending)
            {
                if (!await backend.DestroyOwnEmptyFolderAsync(record.Token, record.FolderId, ct).ConfigureAwait(false)) return false;
                record.FolderId = null;
                await SaveAsync(journal, record, ct).ConfigureAwait(false);
            }
            if (record.ImportPending || record.FolderPending || record.ScriptPending || record.ApplyPending) return false;
            await journal.CompleteAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException) { return false; }
    }

    private static Task SaveAsync(FilterRecoveryJournal journal, FilterWriteRecord record, CancellationToken ct) =>
        journal.SaveAsync(JsonSerializer.Serialize(record), ct);
}
#endif
