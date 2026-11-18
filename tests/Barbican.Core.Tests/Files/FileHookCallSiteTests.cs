// As HookCallSiteTests (Bulwark lib/__tests__/plugin-hook-catalog.test.ts: every catalogued hook is fired somewhere) for the 14 M5 hooks of the files data layer: each has a call site in src/ (a `HookNames.<Name>` reference) and a row in
// spec/plugins/hooks.csv that names it. Kept in the files folder so the shared HookCallSiteTests file is not edited by two streams. M5 stream files-data.

using Barbican.Core.Tests.Plugins;

namespace Barbican.Core.Tests.Files;

public sealed class FileHookCallSiteTests
{
    private static readonly string[] Hooks =
    [
        "onBeforeFileUpload", "onAfterFileUpload", "onFileUploadCancel", "onFileDownload", "onDirectoryCreate", "onBeforeFileDelete", "onAfterFileDelete",
        "onBeforeFileRename", "onFileRename", "onBeforeFileMove", "onFileMove", "onFileCopy", "onFileDuplicate", "onFileFavoriteToggle",
    ];

    [Fact]
    public void Files_data_m5_hooks_have_call_sites()
    {
        var sources = string.Concat(RepoFiles.SourceFiles().Select(File.ReadAllText));
        var rows = RepoFiles.ReadCsv("spec/plugins/hooks.csv")
            .Where(r => string.Equals(r["milestone"], "M5", StringComparison.Ordinal) && string.Equals(r["owner"], "files-data", StringComparison.Ordinal))
            .Select(r => r["name"])
            .ToList();
        Assert.Equal(Hooks.Order(StringComparer.Ordinal), rows.Order(StringComparer.Ordinal));
        var missing = rows.Where(name => !sources.Contains($"HookNames.{char.ToUpperInvariant(name[0])}{name[1..]}", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, $"M5 hooks of files-data without a call site: {string.Join(", ", missing)}");
    }
}
