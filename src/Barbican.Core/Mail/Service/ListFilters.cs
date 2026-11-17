// Mirrors: Rust main.rs:1580 (search conditions: text, notKeyword $seen, hasKeyword $flagged, inMailbox), mail.rs:639 (a folder is
// {inMailbox}); Bulwark lib/jmap/client.ts:3085 getEmails (the simple filter {inMailbox, hasKeyword}); RFC 8621 §4.4.1
// (FilterCondition), RFC 8620 §5.5 (FilterOperator AND). Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Service;

/// <summary>The Email/query filter of a list.</summary>
public static class ListFilters
{
    /// <summary>One condition when there is one, an AND of them otherwise (an empty object for "everything").</summary>
    public static JsonObject For(ListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var conditions = new List<JsonObject>();
        if (query.Filter is { } shaped)
        {
            // the search shaper (M2) already holds the folder and the typed text as conditions (gsearch mailbox_search_filter), so they are not added again
            conditions.Add((JsonObject)shaped.DeepClone());
        }
        else
        {
            if (!string.IsNullOrEmpty(query.MailboxId))
            {
                conditions.Add(new JsonObject { ["inMailbox"] = query.MailboxId });
            }

            if (query.SearchText?.Trim() is { Length: > 0 } text)
            {
                conditions.Add(new JsonObject { ["text"] = text });
            }
        }

        if (!string.IsNullOrEmpty(query.HasKeyword))
        {
            conditions.Add(new JsonObject { ["hasKeyword"] = query.HasKeyword });
        }

        if (!string.IsNullOrEmpty(query.NotKeyword))
        {
            conditions.Add(new JsonObject { ["notKeyword"] = query.NotKeyword });
        }

        return conditions.Count switch
        {
            0 => [],
            1 => conditions[0],
            _ => new JsonObject { ["operator"] = "AND", ["conditions"] = new JsonArray([.. conditions]) },
        };
    }

    /// <summary>All conditions joined with AND.</summary>
    public static JsonObject And(params JsonObject[] conditions) =>
        new() { ["operator"] = "AND", ["conditions"] = new JsonArray([.. conditions.Select(c => (JsonNode)c.DeepClone())]) };
}
