// Bulwark lib/jmap/client.ts:3204 getEmailsInMailbox. Collect all root IDs before any write.
using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;

namespace Barbican.Core.Mail.Archive;

internal static class ArchiveSnapshot
{
    internal sealed record Item(string Id, DateTimeOffset? ReceivedAt);

    public static async Task<Result<IReadOnlyList<Item>>> ReadAsync(IJmapClient client, string account, string archive, CancellationToken ct)
    {
        var ids = new List<string>();
        string? state = null;
        int? total = null;
        do
        {
            var args = new JsonObject { ["accountId"] = account, ["filter"] = new JsonObject { ["inMailbox"] = archive },
                ["sort"] = new JsonArray(new JsonObject { ["property"] = "receivedAt", ["isAscending"] = false }),
                ["position"] = ids.Count, ["limit"] = 500, ["calculateTotal"] = true };
            var response = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("Email/query", args, "q")), ct).ConfigureAwait(false);
            if (!response.IsOk) return Result<IReadOnlyList<Item>>.Fail(response.Error!);
            var value = response.Value!.MethodResponses[0][1]!;
            var nextState = value.Str("queryState");
            var nextTotal = value["total"].AsNumber();
            if (string.IsNullOrEmpty(nextState) || nextTotal is not { } count || count > 1_000_000
                || (state is not null && (!string.Equals(state, nextState, StringComparison.Ordinal) || total != (int)count)))
                return Result<IReadOnlyList<Item>>.Fail(new ValidationError("Archive changed during the scan; retry before reorganizing."));
            state = nextState;
            total = (int)count;
            var page = value.Strings("ids");
            if (page.Count == 0 && ids.Count < total) return Result<IReadOnlyList<Item>>.Fail(new ValidationError("Archive scan returned an incomplete page."));
            ids.AddRange(page);
            if (ids.Count > total || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                return Result<IReadOnlyList<Item>>.Fail(new ValidationError("Archive scan returned duplicate or extra messages."));
        }
        while (ids.Count < total);

        var get = await client.GetAllAsync("Email/get", account, ids, new JsonArray("id", "receivedAt"), ct).ConfigureAwait(false);
        if (!get.IsOk) return Result<IReadOnlyList<Item>>.Fail(get.Error!);
        var byId = get.Value!.Where(item => item.Str("id") is not null).ToDictionary(item => item.Str("id")!, StringComparer.Ordinal);
        if (ids.Any(id => !byId.ContainsKey(id))) return Result<IReadOnlyList<Item>>.Fail(new ValidationError("Archive messages changed during the scan; retry."));
        return Result<IReadOnlyList<Item>>.Ok([.. ids.Select(id => new Item(id, DateTimeOffset.TryParse(byId[id].Str("receivedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : null))]);
    }
}
