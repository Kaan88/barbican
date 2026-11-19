using System.Text.Json.Nodes;

namespace Barbican.Presentation.Settings;

/// <summary>Completed writes and their prior values, including a partial result on failure.</summary>
public sealed record AccessibilityQuickApplyResult(IReadOnlyDictionary<string, JsonNode?> Previous, IReadOnlyList<string> Completed, string? Error);
