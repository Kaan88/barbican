// Mirrors: Rust contact_merge.rs:126 MergePlan (keep, others, patch of the properties that change, each in full). Contract from the M5 plan section 4.1; owner con-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Contacts;

/// <summary>How duplicate cards become one.</summary>
public sealed record ContactMergePlan(JsonNode Keep, IReadOnlyList<JsonNode> Others, JsonObject Patch);
