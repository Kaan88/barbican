// Bulwark components/settings/message-list-settings.tsx:82-145; lib/jmap/client.ts:3204, 4134.
// Snapshot the captured account's root before any move; one pair of move hooks per destination.
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Mail.Write;
using Barbican.Core.Mail.Write.Payloads;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Mail.Archive;

/// <summary>Moves root Archive messages into their local-year or local-month folders.</summary>
public sealed class ArchiveReorganizer(IAccountManager accounts, IMailWriterFactory writers, IHookBus hooks, TimeProvider time) : IArchiveReorganizer
{
    public async Task<Result<ArchiveReorganizationReport>> ReorganizeAsync(string accountKey, string mode, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(accountKey);
        if (mode is not ("single" or "year" or "month")) return Result<ArchiveReorganizationReport>.Fail(new ValidationError("Unknown archive mode."));
        var session = accounts.SessionFor(accountKey);
        if (session is null) return Result<ArchiveReorganizationReport>.Fail(new ValidationError("The account is not signed in."));
        if (string.Equals(mode, "single", StringComparison.Ordinal)) return Result<ArchiveReorganizationReport>.Ok(new(0, 0, 0, false, null));
        if (session.Client.IsReadOnly) return Result<ArchiveReorganizationReport>.Fail(new ReadOnlyViolation("archive reorganization"));
        var progress = new Progress();
        try
        {
            return await RunAsync(session, mode, progress, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result<ArchiveReorganizationReport>.FailPartial(new ValidationError("Archive reorganization was cancelled."), progress.Report(true, "The current request may have completed; refresh the folder before retrying."));
        }
    }

    private async Task<Result<ArchiveReorganizationReport>> RunAsync(IAccountSession session, string mode, Progress progress, CancellationToken ct)
    {
        var account = session.Client.Session.MailAccountId;
        if (string.IsNullOrEmpty(account)) return Result<ArchiveReorganizationReport>.Fail(new ValidationError("The account has no mail account."));
        var mail = new MailService(session.Client, time, account);
        var boxesResult = await mail.GetMailboxesAsync(ct).ConfigureAwait(false);
        if (!boxesResult.IsOk) return Result<ArchiveReorganizationReport>.Fail(boxesResult.Error!);
        var boxes = boxesResult.Value!.All.ToList();
        var archive = boxes.FirstOrDefault(box => box.Role == MailboxRole.Archive)
            ?? boxes.FirstOrDefault(box => string.Equals(box.Name, "Archive", StringComparison.OrdinalIgnoreCase));
        if (archive is null) return Result<ArchiveReorganizationReport>.Fail(new ValidationError("There is no Archive folder."));
        var snapshot = await ArchiveSnapshot.ReadAsync(session.Client, account, archive.Id, ct).ConfigureAwait(false);
        if (!snapshot.IsOk) return Result<ArchiveReorganizationReport>.Fail(snapshot.Error!);
        var items = snapshot.Value!;
        progress.Total = items.Count;
        progress.SnapshotComplete = true;
        progress.Skipped += items.Count(item => item.ReceivedAt is null);
        var groups = items.Where(item => item.ReceivedAt is not null)
            .GroupBy(item => string.Join('/', ArchivePath.Of(item.ReceivedAt!.Value, mode, time.LocalTimeZone)), StringComparer.Ordinal);
        var writer = writers.ForSession(session, account);
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            var ids = group.Select(item => item.Id).ToArray();
            var destination = await EnsureAsync(writer, boxes, archive.Id, group.Key.Split('/'), ct).ConfigureAwait(false);
            if (!destination.IsOk) return Result<ArchiveReorganizationReport>.FailPartial(destination.Error!, progress.Report(false, "Folder creation failed; remaining messages were not attempted."));
            if (destination.Value is null) { progress.Skipped += ids.Length; continue; }
            var result = await MoveGroupAsync(writer, session, archive.Id, destination.Value, ids, progress, ct).ConfigureAwait(false);
            if (!result.IsOk) return Result<ArchiveReorganizationReport>.FailPartial(result.Error!, progress.Report(false, "A move failed; refresh before retrying because the request outcome may be unknown."));
        }

        return Result<ArchiveReorganizationReport>.Ok(progress.Report(false, progress.Failed > 0 ? "Some messages were refused by the server." : null));
    }

    private async Task<Result<string?>> EnsureAsync(IMailWriter writer, List<Mailbox> boxes, string archive, IReadOnlyList<string> path, CancellationToken ct)
    {
        var parent = archive;
        foreach (var name in path)
        {
            var found = boxes.FirstOrDefault(box => string.Equals(box.ParentId, parent, StringComparison.Ordinal) && string.Equals(box.Name, name, StringComparison.Ordinal));
            if (found is null)
            {
                if (!await hooks.AllowAsync(HookNames.OnBeforeMailboxCreate, () => new MailboxCreatePayload(name, parent), ct).ConfigureAwait(false)) return Result<string?>.Ok(null);
                var created = await writer.CreateMailboxAsync(name, parent, null, ct).ConfigureAwait(false);
                if (!created.IsOk) return Result<string?>.Fail(created.Error!);
                found = created.Value!;
                boxes.Add(found);
                var folderParent = parent;
                hooks.Fire(HookNames.OnMailboxCreate, () => new MailboxCreatedPayload(found.Id, name, folderParent));
            }

            parent = found.Id;
        }

        return Result<string?>.Ok(parent);
    }

    private async Task<Result<Unit>> MoveGroupAsync(IMailWriter writer, IAccountSession session, string archive, string destination, string[] ids, Progress progress, CancellationToken ct)
    {
        var payload = new EmailMovePayload(ids, archive, destination);
        if (!await hooks.AllowAsync(HookNames.OnBeforeEmailMove, () => payload, ct).ConfigureAwait(false))
        {
            progress.Skipped += ids.Length;
            return Result<Unit>.Ok(default);
        }

        var updated = new List<string>();
        var max = Math.Max(1, session.Client.Session.Limits.MaxObjectsInSet);
        try
        {
            foreach (var chunk in ids.Chunk(max))
            {
                ct.ThrowIfCancellationRequested();
                progress.InFlight = chunk.Length;
                var moved = await writer.MoveAsync(chunk, null, destination, ct).ConfigureAwait(false);
                if (moved.Value is { } report)
                {
                    updated.AddRange(report.Updated);
                    progress.Moved += report.Updated.Count;
                    progress.Failed += report.Refused.Count;
                    progress.InFlight = Math.Max(0, chunk.Length - report.Updated.Count - report.Refused.Count);
                }

                if (!moved.IsOk) return Result<Unit>.Fail(moved.Error!);
                if (progress.InFlight != 0) return Result<Unit>.Fail(new ValidationError("The server did not report every message outcome."));
            }
        }
        finally
        {
            if (updated.Count > 0) hooks.Fire(HookNames.OnAfterEmailMove, () => new EmailMovePayload(updated, archive, destination));
        }

        return Result<Unit>.Ok(default);
    }

    private sealed class Progress
    {
        public bool SnapshotComplete { get; set; }
        public int Total { get; set; }
        public int Moved { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public int InFlight { get; set; }
        public ArchiveReorganizationReport Report(bool cancelled, string? detail) => new(Moved, Skipped, Failed, cancelled, detail)
        {
            Total = Total,
            SnapshotComplete = SnapshotComplete,
            Unknown = InFlight,
            Unattempted = Math.Max(0, Total - Moved - Skipped - Failed - InFlight),
        };
    }
}
