// The read side of the fake store: folder counts (RFC 8621 §2), threads (§3), queries over the current or a past message index
// (RFC 8620 §5.5, §5.6), and the change log (RFC 8620 §5.2, with RFC 8621 §2.2 updatedProperties). Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.State;

namespace Barbican.FakeJmap;

public sealed partial class FakeStore
{
    /// <summary>Every folder with its counts.</summary>
    public IReadOnlyList<JsonObject> Mailboxes()
    {
        lock (_lock)
        {
            return [.. _mailboxes.Select(WithCounts)];
        }
    }

    /// <summary>The full objects of the given messages (unknown ids are skipped).</summary>
    public IReadOnlyList<JsonObject> Emails(IEnumerable<string> ids)
    {
        lock (_lock)
        {
            return [.. ids.Select(i => _emails.TryGetValue(i, out var e) ? (JsonObject)e.DeepClone() : null).OfType<JsonObject>()];
        }
    }

    /// <summary>The thread with its message ids, oldest first; null when it does not exist.</summary>
    public JsonObject? Thread(string id)
    {
        lock (_lock)
        {
            var ids = _emails.Values.Where(e => string.Equals((string?)e["threadId"], id, StringComparison.Ordinal))
                .OrderBy(e => (string?)e["receivedAt"], StringComparer.Ordinal).Select(e => (JsonNode)(string)e["id"]!).ToList();
            return ids.Count == 0 ? null : new JsonObject { ["id"] = id, ["emailIds"] = new JsonArray([.. ids]) };
        }
    }

    /// <summary>The ids matching <paramref name="filter"/> now, newest first.</summary>
    public IReadOnlyList<string> Query(JsonNode? filter, JsonArray? sort = null)
    {
        lock (_lock)
        {
            return Sorted(_emails.Values, filter, sort);
        }
    }

    /// <summary>The ids that matched at Email state <paramref name="state"/>; null when that state is unknown.</summary>
    public IReadOnlyList<string>? QueryAt(string state, JsonNode? filter, JsonArray? sort = null)
    {
        lock (_lock)
        {
            return StalwartState.ChangeId(state) is { } id && _snapshots.TryGetValue(id, out var snap) ? Sorted(snap, filter, sort) : null;
        }
    }

    /// <summary>The changes of <paramref name="type"/> since <paramref name="since"/>; null when they cannot be calculated.</summary>
    public FakeChanges? Changes(string type, string since, int? max)
    {
        lock (_lock)
        {
            if (StalwartState.ChangeId(since) is not { } from || from > _change)
            {
                return null;
            }

            var entries = new List<FakeChange>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var hasMore = false;
            foreach (var group in _log.Where(c => string.Equals(c.Type, type, StringComparison.Ordinal) && c.ChangeId > from).GroupBy(c => c.ChangeId))
            {
                var next = new HashSet<string>(ids, StringComparer.Ordinal);
                next.UnionWith(group.Select(c => c.Id));
                if (max is { } m && next.Count > m && entries.Count > 0)
                {
                    hasMore = true;
                    break;
                }

                ids = next;
                entries.AddRange(group);
            }

            var newState = hasMore ? entries[^1].ChangeId : _typeState.GetValueOrDefault(type, 1UL);
            return FakeChanges.From(since, StalwartState.Encode(Math.Max(newState, from)), hasMore, entries);
        }
    }

    private static List<string> Sorted(IEnumerable<JsonObject> emails, JsonNode? filter, JsonArray? sort)
    {
        var comparators = sort?.OfType<JsonObject>().ToList() ?? [];
        if (comparators.Count == 0)
        {
            comparators.Add(new JsonObject { ["property"] = "receivedAt", ["isAscending"] = false });
        }

        return [.. emails.Where(e => FilterEvaluator.Matches(e, filter))
            .OrderBy(e => e, Comparer<JsonObject>.Create((a, b) => Compare(a, b, comparators)))
            .ThenByDescending(e => (string?)e["id"], StringComparer.Ordinal)
            .Select(e => (string)e["id"]!)];
    }

    private static int Compare(JsonObject a, JsonObject b, IReadOnlyList<JsonObject> comparators)
    {
        foreach (var comparator in comparators)
        {
            var property = (string?)comparator["property"] ?? "receivedAt";
            int result;
            if (string.Equals(property, "hasKeyword", StringComparison.Ordinal))
            {
                var keyword = (string?)comparator["keyword"] ?? "";
                result = (a["keywords"]?[keyword] is not null).CompareTo(b["keywords"]?[keyword] is not null);
            }
            else if (string.Equals(property, "size", StringComparison.Ordinal))
            {
                result = ((long?)a[property] ?? 0).CompareTo((long?)b[property] ?? 0);
            }
            else
            {
                result = string.Compare(SortText(a[property]), SortText(b[property]), StringComparison.OrdinalIgnoreCase);
            }

            if (result != 0)
            {
                return (bool?)comparator["isAscending"] == true ? result : -result;
            }
        }

        return 0;
    }

    private static string? SortText(JsonNode? value) => value switch
    {
        JsonValue v when v.TryGetValue<string>(out var text) => text,
        JsonArray list when list.FirstOrDefault() is JsonObject person => (string?)person["name"] ?? (string?)person["email"],
        _ => null,
    };

    private JsonObject WithCounts(JsonObject box)
    {
        var id = (string)box["id"]!;
        var inBox = _emails.Values.Where(e => e["mailboxIds"]?[id] is not null).ToList();
        bool Unread(JsonObject e) => e["keywords"]?["$seen"] is null;
        var o = (JsonObject)box.DeepClone();
        o["totalEmails"] = inBox.Count;
        o["unreadEmails"] = inBox.Count(Unread);
        o["totalThreads"] = inBox.Select(e => (string?)e["threadId"]).Distinct(StringComparer.Ordinal).Count();
        o["unreadThreads"] = inBox.Where(Unread).Select(e => (string?)e["threadId"]).Distinct(StringComparer.Ordinal).Count();
        return o;
    }
}
