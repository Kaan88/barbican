// Mirrors: Stalwart crates/jmap/src/registry/mapping/archived_item.rs (x:ArchivedItem get, query, set with status requestRestore; query takes the optional
// accountId filter that a normal user is refused with unsupportedFilter). Answers only with FakeJmapOptions.ArchiveCapability; the store starts empty
// and a test fills it with AddArchived. Restoring removes the item (the server moves the message back). J-074. Owner close-b.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private readonly List<JsonObject> _archived = [];

    /// <summary>Adds a deleted item to the archive: its id, the account it belonged to, when it was archived and the message's subject.</summary>
    public void AddArchived(string id, DateTimeOffset archivedAt, string subject)
    {
        lock (_lock)
        {
            _archived.Add(new JsonObject
            {
                ["id"] = id,
                ["accountId"] = options.AccountId,
                ["archivedAt"] = archivedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["archivedUntil"] = archivedAt.AddDays(30).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["subject"] = subject,
                ["status"] = "archived",
            });
        }
    }

    /// <summary>The ids still in the archive.</summary>
    public IReadOnlyList<string> ArchivedIds
    {
        get
        {
            lock (_lock)
            {
                return [.. _archived.Select(a => (string)a["id"]!)];
            }
        }
    }

    private (string? Name, JsonObject Args) ArchivedItemGet(JsonObject args)
    {
        if (!options.ArchiveCapability)
        {
            return Error("unknownMethod");
        }

        lock (_lock)
        {
            var ids = args["ids"] is JsonArray a ? a.Select(x => (string?)x).OfType<string>().ToHashSet(StringComparer.Ordinal) : null;
            var list = _archived.Where(n => ids is null || ids.Contains((string)n["id"]!)).Select(n => (JsonNode)n.DeepClone());
            var notFound = ids is null ? [] : ids.Where(i => !_archived.Any(n => string.Equals((string?)n["id"], i, StringComparison.Ordinal))).Select(i => (JsonNode)i);
            return (null, new JsonObject { ["accountId"] = options.AccountId, ["state"] = "a1", ["list"] = new JsonArray([.. list]), ["notFound"] = new JsonArray([.. notFound]) });
        }
    }

    private (string? Name, JsonObject Args) ArchivedItemQuery(JsonObject args)
    {
        if (!options.ArchiveCapability)
        {
            return Error("unknownMethod");
        }

        if (args["filter"] is not null && options.ArchiveRefusesAccountFilter)
        {
            return Error("unsupportedFilter");
        }

        lock (_lock)
        {
            var ids = _archived.Select(a => (string)a["id"]!).ToList();
            var position = (int?)args["position"] ?? 0;
            var limit = (int?)args["limit"] ?? ids.Count;
            var page = ids.Skip(position).Take(limit).Select(i => (JsonNode)i);
            return (null, new JsonObject { ["accountId"] = options.AccountId, ["queryState"] = "a1", ["position"] = position, ["total"] = ids.Count, ["ids"] = new JsonArray([.. page]) });
        }
    }

    private (string? Name, JsonObject Args) ArchivedItemSet(JsonObject args)
    {
        if (!options.ArchiveCapability)
        {
            return Error("unknownMethod");
        }

        lock (_lock)
        {
            var updated = new JsonObject();
            var notUpdated = new JsonObject();
            foreach (var (id, change) in args["update"] as JsonObject ?? [])
            {
                var item = _archived.FirstOrDefault(a => string.Equals((string?)a["id"], id, StringComparison.Ordinal));
                if (item is null)
                {
                    notUpdated[id] = new JsonObject { ["type"] = "notFound" };
                }
                else if (!string.Equals((string?)change?["status"], "requestRestore", StringComparison.Ordinal))
                {
                    notUpdated[id] = new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("status") };
                }
                else
                {
                    _archived.Remove(item);
                    updated[id] = null;
                }
            }

            return (null, new JsonObject { ["accountId"] = options.AccountId, ["oldState"] = "a1", ["newState"] = "a2", ["updated"] = updated, ["notUpdated"] = notUpdated });
        }
    }
}
