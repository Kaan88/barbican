// Mirrors: Rust jmap.rs value reads (`m["id"].as_str()`, missing members are defaults); RFC 8620 §5.1 (a /get list), RFC 8621 §4.1
// (Email properties, `header:Authentication-Results:all`); PORT-PLAN.md §3.4 "Models". Owner jmap.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Jmap.Json;

/// <summary>Turns server objects into model records. A JSON <c>null</c> member counts as absent, so the record's default stays.</summary>
public static class ModelReader
{
    /// <summary>A list row; null when the object has no id or does not fit the model.</summary>
    public static EmailSummary? Summary(JsonNode? node) => Read(node, JmapJsonContext.Default.EmailSummary);

    /// <summary>A full message for the reader; <see cref="EmailBody.IsTruncated"/> is set when any fetched body value was cut.</summary>
    public static EmailBody? Body(JsonNode? node)
    {
        var body = Read(node, JmapJsonContext.Default.EmailBody);
        return body is null ? null : body with { IsTruncated = body.BodyValues.Values.Any(v => v.IsTruncated) };
    }

    /// <summary>A mailbox.</summary>
    public static Mailbox? Mailbox(JsonNode? node) => Read(node, JmapJsonContext.Default.Mailbox);

    /// <summary>An identity.</summary>
    public static Identity? Identity(JsonNode? node) => Read(node, JmapJsonContext.Default.Identity);

    private static T? Read<T>(JsonNode? node, JsonTypeInfo<T> info)
        where T : class
    {
        if (node is not JsonObject o || o["id"] is not JsonValue)
        {
            return null;
        }

        var clean = new JsonObject();
        foreach (var (k, v) in o)
        {
            if (v is not null)
            {
                clean[k] = v.DeepClone();
            }
        }

        try
        {
            return clean.Deserialize(info);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
