// As Bulwark's lib/__tests__/plugin-hook-catalog.test.ts: every catalogued hook is fired somewhere. Here: every hook with milestone M1
// has at least one call site in src/ (a `HookNames.<Name>` reference). One test per owning stream, red until that stream fires its hooks;
// the stream removes the [Red] attribute of its test in the commit that completes its hooks.

namespace Barbican.Core.Tests.Plugins;

public sealed class HookCallSiteTests
{
    private static void AssertFired(string owner)
    {
        var sources = string.Concat(RepoFiles.SourceFiles().Select(File.ReadAllText));
        var missing = RepoFiles.ReadCsv("spec/plugins/hooks.csv")
            .Where(r => string.Equals(r["milestone"], "M1", StringComparison.Ordinal) && string.Equals(r["owner"], owner, StringComparison.Ordinal))
            .Select(r => r["name"])
            .Where(name => !sources.Contains($"HookNames.{char.ToUpperInvariant(name[0])}{name[1..]}", StringComparison.Ordinal))
            .ToList();
        Assert.True(missing.Count == 0, $"M1 hooks of {owner} without a call site: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Jmap_hooks_have_call_sites() => AssertFired("jmap");

    [Fact]
    [Red("accounts")]
    public void Accounts_hooks_have_call_sites() => AssertFired("accounts");

    [Fact]
    public void Mailcore_hooks_have_call_sites() => AssertFired("mailcore");

    [Fact]
    public void Shell_ui_hooks_have_call_sites() => AssertFired("shell-ui");

    [Fact]
    [Red("body")]
    public void Body_hooks_have_call_sites() => AssertFired("body");
}
