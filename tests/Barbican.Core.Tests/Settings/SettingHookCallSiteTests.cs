// As HookCallSiteTests (Bulwark lib/__tests__/plugin-hook-catalog.test.ts: every catalogued hook is fired somewhere) for the 21 M6 hooks of set-data: each has a call site in src/ (a `HookNames.<Name>` reference) and a row in spec/plugins/hooks.csv that names it.
// onSettingsSync keeps its row but is `deferred(M10)` with settings sync (G-064, D-M6-A04): its call site is the one place the hook would fire, left as a comment-free reference in SettingValues. M6 stream set-data.

using Barbican.Core.Tests.Plugins;

namespace Barbican.Core.Tests.Settings;

public sealed class SettingHookCallSiteTests
{
    private static readonly string[] Hooks =
    [
        "onBeforeSettingChange", "onSettingChange", "onSettingsExport", "onSettingsImport", "onSettingsReset", "onSettingsSync", "onKeywordChange", "onTrustedSenderChange",
        "onIdentitiesLoaded", "onBeforeIdentityChange", "onIdentityCreate", "onIdentityUpdate", "onIdentityDelete", "onIdentitySelect", "onSignatureRender", "onSelectReplyIdentity",
        "onPasswordChange", "onTotpChange", "onAppPasswordChange", "onEncryptionChange", "onDisplayNameChange",
    ];

    [Fact]
    public void Set_data_m6_hooks_have_call_sites()
    {
        var sources = string.Concat(RepoFiles.SourceFiles().Select(File.ReadAllText));
        var rows = RepoFiles.ReadCsv("spec/plugins/hooks.csv")
            .Where(r => string.Equals(r["milestone"], "M6", StringComparison.Ordinal) && string.Equals(r["owner"], "set-data", StringComparison.Ordinal))
            .Select(r => r["name"])
            .ToList();
        Assert.Equal(Hooks.Order(StringComparer.Ordinal), rows.Order(StringComparer.Ordinal));
        var missing = rows.Where(name => !sources.Contains($"HookNames.{char.ToUpperInvariant(name[0])}{name[1..]}", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, $"M6 hooks of set-data without a call site: {string.Join(", ", missing)}");
    }
}
