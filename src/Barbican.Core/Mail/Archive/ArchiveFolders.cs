// Mirrors: Rust mail.rs:1074 archive_dated (walk the path under the Archive folder, creating each missing level, then group the messages by
// destination); Bulwark lib/jmap/client.ts archiveEmails :4215. Stalwart crates/jmap/src/mailbox/set.rs (create with parentId).

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write;

namespace Barbican.Core.Mail.Archive;

/// <summary>Finds or creates the dated folders under Archive.</summary>
public static class ArchiveFolders
{
    /// <summary>
    /// The id of the folder at <paramref name="path"/> under <paramref name="archiveId"/>, created level by level when missing. The created
    /// folders are appended to <paramref name="boxes"/> so the next message of the same month finds them.
    /// </summary>
    public static async Task<Result<string>> EnsureAsync(IMailWriter writer, ICollection<Mailbox> boxes, string archiveId, IReadOnlyList<string> path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(boxes);
        var parent = archiveId;
        foreach (var name in path)
        {
            var found = boxes.FirstOrDefault(m => string.Equals(m.ParentId, parent, StringComparison.Ordinal) && string.Equals(m.Name, name, StringComparison.Ordinal));
            if (found is null)
            {
                var created = await writer.CreateMailboxAsync(name, parent, null, ct).ConfigureAwait(false);
                if (!created.IsOk)
                {
                    return Result<string>.Fail(created.Error ?? new ValidationError("The folder could not be created."));
                }

                boxes.Add(created.Value!);
                found = created.Value!;
            }

            parent = found.Id;
        }

        return Result<string>.Ok(parent);
    }
}
