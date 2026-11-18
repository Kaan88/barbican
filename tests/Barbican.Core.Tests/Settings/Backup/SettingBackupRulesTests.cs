// Not Rust rows: B-SET-006 (backup and restore accept Bulwark exports); the file side is T-259 (green, Core/Settings/Prefs). M6 stream set-data.

using System.Text.Json.Nodes;
using Barbican.Core.Settings.Backup;
using Barbican.Core.Settings.Pages;

namespace Barbican.Core.Tests.Settings.Backup;

public sealed class SettingBackupRulesTests
{
    private static readonly SettingPage[] Pages =
    [
        new("general", "General", "top",
        [
            new SettingRow("S-theme", "general", SettingRowKind.Select, "Theme", string.Empty, "theme", [], null, SettingRowStatus.Wired, false, null),
            new SettingRow("S-fontSize", "general", SettingRowKind.Select, "Font size", string.Empty, "fontSize", [], null, SettingRowStatus.Wired, false, null),
        ]),
    ];

    [Fact]
    public void Flat_wrapped_and_barbican_files_are_told_apart_and_unknown_keys_are_listed()
    {
        var flat = SettingBackupRules.Plan(JsonNode.Parse("""{"theme": "dark", "fontSize": "large", "pluginThing": 1}""")!, Pages)!;
        Assert.True(flat.IsBulwark);
        Assert.False(flat.HasDesktop);
        Assert.Equal(2, flat.KnownKeys);
        Assert.Equal(["pluginThing"], flat.UnknownKeys);

        var wrapped = SettingBackupRules.Plan(JsonNode.Parse("""{"settings": {"theme": "dark"}}""")!, Pages)!;
        Assert.True(wrapped.IsBulwark);
        Assert.Equal(1, wrapped.KnownKeys);

        var own = SettingBackupRules.Plan(JsonNode.Parse("""{"app": "barbican", "version": 1, "settings": {"theme": "dark"}, "desktop": {"provider": 1}}""")!, Pages)!;
        Assert.False(own.IsBulwark);
        Assert.True(own.HasDesktop);

        Assert.Null(SettingBackupRules.Plan(JsonNode.Parse("[1, 2]")!, Pages));
    }
}
