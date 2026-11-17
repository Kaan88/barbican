// Mirrors: Rust main.rs:560 check_new_mail (role ids; unread mail in the inbox, or with pushNotifyInboxOnly off in every folder but
// junk, trash, sent and drafts; the newest 10; the first run only records what is there; the rest is new); Bulwark
// stores/email-store.ts:1301 (onNewEmailReceived for each new message), lib/desktop/mail-notifications.ts. Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;

namespace Barbican.Core.Sync;

/// <summary>Finds mail that arrived since the last look.</summary>
public sealed class NewMailWatcher(MailService service)
{
    private const int Window = 10;
    private static readonly string[] Skipped = ["junk", "trash", "sent", "drafts"];
    private readonly Lock _lock = new();
    private HashSet<string>? _seen;

    /// <summary>Only the inbox counts (Bulwark pushNotifyInboxOnly).</summary>
    public bool InboxOnly { get; set; }

    /// <summary>The messages new since the last call, newest first; the first call only seeds and returns none.</summary>
    public async Task<Result<IReadOnlyList<EmailSummary>>> CheckAsync(CancellationToken ct)
    {
        var roles = await service.GetRoleIdsAsync(ct).ConfigureAwait(false);
        if (!roles.IsOk)
        {
            return Result<IReadOnlyList<EmailSummary>>.Fail(roles.Error!);
        }

        if (!roles.Value!.TryGetValue("inbox", out var inbox))
        {
            return Result<IReadOnlyList<EmailSummary>>.Ok([]);
        }

        var place = InboxOnly
            ? new JsonObject { ["inMailbox"] = inbox }
            : new JsonObject { ["inMailboxOtherThan"] = new JsonArray([.. Skipped.Select(r => roles.Value.TryGetValue(r, out var id) ? id : null).OfType<string>().Select(x => (JsonNode)x)]) };
        var filter = ListFilters.And(place, new JsonObject { ["notKeyword"] = "$seen" });
        var page = await ListPageFetcher.FetchAsync(service.Client, filter, null, Window, ct).ConfigureAwait(false);
        return page.Map(p => (IReadOnlyList<EmailSummary>)Fresh(p.Items));
    }

    private List<EmailSummary> Fresh(IReadOnlyList<EmailSummary> newestFirst)
    {
        lock (_lock)
        {
            var first = _seen is null;
            _seen ??= new HashSet<string>(StringComparer.Ordinal);
            var fresh = new List<EmailSummary>();
            for (var i = newestFirst.Count - 1; i >= 0; i--)
            {
                if (_seen.Add(newestFirst[i].Id) && !first)
                {
                    fresh.Add(newestFirst[i]);
                }
            }

            fresh.Reverse();
            return fresh;
        }
    }
}
