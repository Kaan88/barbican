// As Bulwark's lib/__tests__/plugin-hook-catalog.test.ts: every catalogued hook is fired somewhere. Here: every hook with milestone M1 or M2
// has at least one call site in src/ (a `HookNames.<Name>` reference). One test per owning stream, red until that stream fires its hooks;
// the stream removes the [Red] attribute of its test in the commit that completes its hooks.

namespace Barbican.Core.Tests.Plugins;

public sealed class HookCallSiteTests
{
    private static void AssertFired(string owner, string milestone = "M1")
    {
        var sources = string.Concat(RepoFiles.SourceFiles().Select(File.ReadAllText));
        var missing = RepoFiles.ReadCsv("spec/plugins/hooks.csv")
            .Where(r => string.Equals(r["milestone"], milestone, StringComparison.Ordinal) && string.Equals(r["owner"], owner, StringComparison.Ordinal))
            .Select(r => r["name"])
            .Where(name => !sources.Contains($"HookNames.{char.ToUpperInvariant(name[0])}{name[1..]}", StringComparison.Ordinal))
            .ToList();
        Assert.True(missing.Count == 0, $"{milestone} hooks of {owner} without a call site: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Jmap_hooks_have_call_sites() => AssertFired("jmap");

    [Fact]
    public void Accounts_hooks_have_call_sites() => AssertFired("accounts");

    [Fact]
    public void Mailcore_hooks_have_call_sites() => AssertFired("mailcore");

    [Fact]
    public void Shell_ui_hooks_have_call_sites() => AssertFired("shell-ui");

    [Fact]
    public void Body_hooks_have_call_sites() => AssertFired("body");

    // M2 (stage F of M2, plan 2.4): one red test per owner, removed by the stream in the commit that fires its hooks (actions 28, reader 4, search 1, unified 3).
    [Fact]
    public void Actions_m2_hooks_have_call_sites() => AssertFired("actions", "M2");

    [Fact]
    public void Reader_m2_hooks_have_call_sites() => AssertFired("reader", "M2");

    [Fact]
    public void Search_m2_hooks_have_call_sites() => AssertFired("search", "M2");

    [Fact]
    public void Unified_m2_hooks_have_call_sites() => AssertFired("unified", "M2");

    // M3: stream send-pipeline fires 15 hooks (drafts, seeding, send, attachment upload).
    [Fact]
    public void Send_pipeline_m3_hooks_have_call_sites() => AssertFired("send-pipeline", "M3");

    // M3: stream composer-ui fires 10 hooks (reply and forward intercepts, composer open, close and change, recipient chips, mailto).
    [Fact]
    public void Composer_ui_m3_hooks_have_call_sites() => AssertFired("composer-ui", "M3");

    // M4: stream cal-views fires 5 hooks (event open, date change, view change, calendar change, visibility toggle).
    [Fact]
    public void Cal_views_m4_hooks_have_call_sites() => AssertFired("cal-views", "M4");
}
