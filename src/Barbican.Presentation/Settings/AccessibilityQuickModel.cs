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
}
