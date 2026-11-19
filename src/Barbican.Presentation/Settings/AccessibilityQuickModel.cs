// Mirrors: Bulwark components/accessibility/accessibility-quick-panel-body.tsx and lib/accessibility-presets.ts (I-417).
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Settings.Pages;

namespace Barbican.Presentation.Settings;

/// <summary>Uses the same profile writer as the full settings page.</summary>
public sealed class AccessibilityQuickModel(ISettingValues values)
{
    public JsonNode? Stored(string key) => values.Stored(key);

    public Task<Result<Unit>> SetAsync(string key, JsonNode? value, CancellationToken ct) => values.SetAsync(key, value, ct);

    /// <summary>Stops on failure and reports exactly which entries are safe to remove from an undo list.</summary>
    public async Task<AccessibilityQuickApplyResult> ApplyAsync(IEnumerable<KeyValuePair<string, JsonNode?>> changes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var previous = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var completed = new List<string>();
        foreach (var (key, value) in changes)
        {
            var old = values.Stored(key);
            if (!JsonNode.DeepEquals(old, value))
            {
                var result = await values.SetAsync(key, value?.DeepClone(), ct).ConfigureAwait(true);
                if (!result.IsOk) return new AccessibilityQuickApplyResult(previous, completed, result.Error?.Message ?? "Could not save every setting.");
                previous[key] = old?.DeepClone();
            }

            completed.Add(key);
        }

        return new AccessibilityQuickApplyResult(previous, completed, null);
    }
}
