// Mirrors: see SettingBackupPlan; Rust settings.rs:167 import (flat or wrapped). Contract from the M6 plan section 4.3; owner set-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Settings.Pages;

namespace Barbican.Core.Settings.Backup;

/// <summary>The pure part of import.</summary>
public static class SettingBackupRules
{
    /// <summary>Classifies an export document: a flat Bulwark object, a Bulwark object wrapped in <c>settings</c>, or a Barbican file (<c>app</c> = barbican with <c>settings</c> and <c>desktop</c>); null for anything that is not a JSON object.</summary>
    public static SettingBackupPlan? Plan(JsonNode document, IReadOnlyList<SettingPage> pages) => throw new PortPendingException("SettingBackupRules.Plan");
}
