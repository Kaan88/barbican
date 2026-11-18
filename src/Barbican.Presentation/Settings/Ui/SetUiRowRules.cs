// Mirrors: Bulwark settings-section.tsx (a section with no visible row is hidden), settings-app.tsx ("Show advanced settings", I-398), components/settings/settings-search anchors. Contract from the M6 plan section 4.10; owner set-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Settings.Pages;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>The pure rules of how rows are shown.</summary>
public static class SetUiRowRules
{
    /// <summary>The rows that show: advanced rows only with <paramref name="showAdvanced"/>, and a section row only when at least one row after it (up to the next section) shows. Order is kept.</summary>
    public static IReadOnlyList<SettingRow> Visible(IReadOnlyList<SettingRow> rows, bool showAdvanced) => throw new PortPendingException("SetUiRowRules.Visible");

    /// <summary>The stored value, else the row's default.</summary>
    public static JsonNode? EffectiveValue(SettingRow row, JsonNode? stored) => throw new PortPendingException("SetUiRowRules.EffectiveValue");

    /// <summary>The text a select row shows for a value: the label of the matching option, else the value itself, else the label of the first option for an unset value.</summary>
    public static string SelectText(SettingRow row, JsonNode? value) => throw new PortPendingException("SetUiRowRules.SelectText");
}
