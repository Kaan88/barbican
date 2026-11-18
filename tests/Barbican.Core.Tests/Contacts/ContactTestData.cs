// Helpers of the M5 contact tests (Rust's json! macro). Owner con-data; no other stream's test project folder uses this name.

using System.Text.Json.Nodes;

namespace Barbican.Core.Tests.Contacts;

internal static class ContactTestData
{
    public static JsonNode J(string json) => JsonNode.Parse(json)!;

    public static JsonObject O(string json) => (JsonObject)JsonNode.Parse(json)!;

    public static IReadOnlyList<JsonNode> Values(JsonNode? map) => [.. map!.AsObject().Select(kv => kv.Value!)];

    public static JsonNode First(JsonNode? map) => Values(map)[0];

    /// <summary>The ids of a group list as "0,1;2,3,4".</summary>
    public static string Show(IReadOnlyList<IReadOnlyList<int>> groups) => string.Join(';', groups.Select(g => string.Join(',', g)));
}
