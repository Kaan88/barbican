// Mirrors: Rust mailimport.rs:281 import_files (each file streamed, .eml capped at 200 MB, progress per message, cancel between batches,
// one outcome for all files), :240 Server over the live client (upload, Email/import, the Message-IDs already in the folder); Bulwark
// lib/mail-import/engine.ts (the folder must allow adding: myRights.mayAddItems). RFC 8621 §2 (myRights), §4.8 (Email/import).
// M2 plan 6.5: the loop goes through the actions' IMailWriter (upload and Email/import), so read-only accounts and the write hooks
// apply as for any other write. Owner unified.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write;

namespace Barbican.Core.Mail.Import;

/// <summary>Imports .eml and .mbox files into a folder of a signed-in account.</summary>
/// <param name="accountOf">The writer and client of the target's account, or null when it is not signed in.</param>
public sealed class MailImportService(Func<MailboxRef, (IMailWriter Writer, IJmapClient Client)?> accountOf) : IMailImport
{
    /// <summary>The refusal of a folder the user may not add messages to.</summary>
    public const string NotAllowed = "You cannot add messages to this folder.";

    /// <inheritdoc />
    public async Task<Result<ImportOutcome>> ImportFilesAsync(MailboxRef target, IReadOnlyList<string> paths, IProgress<int> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(progress);
        if (accountOf(target) is not var (writer, client))
        {
            return Result<ImportOutcome>.Fail(new ValidationError("That account is not signed in."));
        }

        var allowed = await MayAddAsync(client, target.MailboxId, ct).ConfigureAwait(false);
        if (!allowed.IsOk)
        {
            return Result<ImportOutcome>.Fail(allowed.Error!);
        }

        if (!allowed.Value)
        {
            return Result<ImportOutcome>.Fail(new ValidationError(NotAllowed));
        }

        var server = new WriterImportServer(writer, client);
        var (imported, already, errors, done) = (0, 0, new List<string>(), 0);
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var closing = file.ConfigureAwait(false);
            var reader = new MboxReader(Path.GetFileName(path), file, MboxReader.MaxEmlBytes);
            var before = done;
            var outcome = await MailImportLoop.ImportAsync(server, target.MailboxId, Items(reader), n => progress.Report(before + n), ct).ConfigureAwait(false);
            imported += outcome.Imported;
            already += outcome.Already;
            errors.AddRange(outcome.Errors);
            done = before + outcome.Imported + outcome.Already + outcome.Errors.Count;
            if (reader.TooBig)
            {
                errors.Add($"{Path.GetFileName(path)}: larger than {MboxReader.MaxEmlBytes / 1_000_000} MB.");
            }
        }

        return Result<ImportOutcome>.Ok(new ImportOutcome(imported, already, errors));
    }

    /// <summary>True when the folder allows adding messages (a folder without myRights allows it, RFC 8621 §2).</summary>
    public static async Task<Result<bool>> MayAddAsync(IJmapClient client, string mailboxId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        var b = new JmapRequestBuilder();
        var get = b.Add("Mailbox/get", new JsonObject { ["accountId"] = client.Session.MailAccountId, ["ids"] = new JsonArray(mailboxId), ["properties"] = new JsonArray("id", "myRights") });
        var sent = await client.SendAsync(b.Build(), ct).ConfigureAwait(false);
        var args = sent.IsOk ? sent.Value!.Args(get.CallId, get.Method) : Result<JsonObject>.Fail(sent.Error!);
        if (!args.IsOk)
        {
            return Result<bool>.Fail(args.Error!);
        }

        if (args.Value!.Arr("list") is not { Count: > 0 } list)
        {
            return Result<bool>.Fail(new ValidationError("That folder no longer exists."));
        }

        return Result<bool>.Ok(list[0]?["myRights"]?["mayAddItems"] is not JsonValue v || !v.TryGetValue<bool>(out var may) || may);
    }

    private static IEnumerator<ImportItem> Items(MboxReader reader)
    {
        while (reader.NextItem() is { } item)
        {
            yield return item;
        }
    }
}
