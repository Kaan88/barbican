// A /changes answer of the fake store (RFC 8620 §5.2: an object created and destroyed in the window is reported destroyed only, one
// created and then updated is reported created only). Owner jmap.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

/// <summary>The changes since a state.</summary>
public sealed record FakeChanges(string OldState, string NewState, bool HasMore, IReadOnlyList<string> Created, IReadOnlyList<string> Updated,
    IReadOnlyList<string> Destroyed, IReadOnlyList<string>? UpdatedProperties)
{
    /// <summary>Folds log entries into created / updated / destroyed.</summary>
    public static FakeChanges From(string oldState, string newState, bool hasMore, IReadOnlyList<FakeChange> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var created = new List<string>();
        var updated = new List<string>();
        var destroyed = new List<string>();
        List<string>? props = [];
        foreach (var c in entries)
        {
            switch (c.Kind)
            {
                case "created":
                    created.Add(c.Id);
                    break;
                case "updated" when !created.Contains(c.Id, StringComparer.Ordinal) && !updated.Contains(c.Id, StringComparer.Ordinal):
                    updated.Add(c.Id);
                    break;
                case "destroyed":
                    var wasNew = created.Remove(c.Id);
                    updated.Remove(c.Id);
                    if (!wasNew)
                    {
                        destroyed.Add(c.Id);
                    }

                    break;
            }

            if (string.Equals(c.Kind, "updated", StringComparison.Ordinal))
            {
                props = props is not null && c.Properties is { } p ? [.. props.Union(p, StringComparer.Ordinal)] : null;
            }
        }

        return new FakeChanges(oldState, newState, hasMore, created, updated, destroyed, updated.Count > 0 ? props : null);
    }

    /// <summary>The /changes response arguments.</summary>
    public JsonObject ToJson(string accountId, bool mailbox)
    {
        var o = new JsonObject
        {
            ["accountId"] = accountId,
            ["oldState"] = OldState,
            ["newState"] = NewState,
            ["hasMoreChanges"] = HasMore,
            ["created"] = new JsonArray([.. Created.Select(x => (JsonNode)x)]),
            ["updated"] = new JsonArray([.. Updated.Select(x => (JsonNode)x)]),
            ["destroyed"] = new JsonArray([.. Destroyed.Select(x => (JsonNode)x)]),
        };
        if (mailbox)
        {
            o["updatedProperties"] = UpdatedProperties is null ? null : new JsonArray([.. UpdatedProperties.Select(x => (JsonNode)x)]);
        }

        return o;
    }
}
