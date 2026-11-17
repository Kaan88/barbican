// M2 exit tests of stream search (plan 6.4): a quick rule against FakeJmap's Sieve: create, merge, covered, hand-edited refused, a validate error is shown.

using System.Text.Json.Nodes;
using Barbican.Core.Filters.QuickRules;
using Barbican.Core.Sieve;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Filters.QuickRules;

public sealed class QuickRuleSaverTests
{
    private static QuickRuleSubject Subject(string sender) => new([sender], "example.org", null);

    private static JsonNode Block(string sender) => QuickRuleBuilder.PresetRule(new QuickRulePreset.Block(new QuickRuleFolder("j", "Junk Mail", "Junk Mail")), Subject(sender), QuickRuleBuilder.NewId());

    private static async Task<(Fake Fake, QuickRuleSaver Saver)> StartAsync(string? existing = null)
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { SieveCapability = true });
        if (existing is not null)
        {
            f.Server.Api.SeedSieve("mine", existing, active: true);
        }

        return (f, new QuickRuleSaver(new SieveScriptStore(f.Client)));
    }

    [Fact]
    public async Task Block_sender_creates_the_script_then_merges_then_reports_covered()
    {
        var (f, saver) = await StartAsync();
        var ct = TestContext.Current.CancellationToken;

        var first = await saver.SaveAsync(Block("spammer@example.org"), ct);
        Assert.True(first.IsOk, first.Error?.Message);
        Assert.Equal("Rule \"Block spammer@example.org\" created", first.Value);
        var text = f.Server.Api.ActiveSieveText()!;
        Assert.Contains("@metadata:begin", text, StringComparison.Ordinal);
        Assert.Contains("address :is \"From\" \"spammer@example.org\"", text, StringComparison.Ordinal);
        Assert.Contains("fileinto \"Junk Mail\";", text, StringComparison.Ordinal);

        // another sender to the same folder: one rule takes both addresses
        var second = await saver.SaveAsync(Block("other@example.org"), ct);
        Assert.Equal("Added to rule \"Block spammer@example.org\"", second.Value);
        Assert.Contains("\"spammer@example.org\", \"other@example.org\"", f.Server.Api.ActiveSieveText(), StringComparison.Ordinal);

        // the same sender again: nothing is written
        var before = f.Server.ApiCalls.Count(c => c.Contains("SieveScript/set", StringComparison.Ordinal));
        var third = await saver.SaveAsync(Block("other@example.org"), ct);
        Assert.Equal("Already covered by \"Block spammer@example.org\"", third.Value);
        Assert.Equal(before, f.Server.ApiCalls.Count(c => c.Contains("SieveScript/set", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_hand_edited_script_is_refused_and_left_alone()
    {
        const string hand = "/* @metadata:begin\n{\"version\": 9, \"rules\": []}\n@metadata:end */\nif true { stop; }";
        var (f, saver) = await StartAsync(hand);
        var r = await saver.SaveAsync(Block("spammer@example.org"), TestContext.Current.CancellationToken);
        Assert.False(r.IsOk);
        Assert.Contains("edited by hand", r.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(hand, f.Server.Api.ActiveSieveText());
    }

    [Fact]
    public async Task The_other_rules_of_the_script_are_kept_and_a_validate_error_is_shown()
    {
        const string mine = "# mine\nif header :contains \"subject\" \"x\" { discard; }\n";
        var (f, saver) = await StartAsync(mine);
        var ct = TestContext.Current.CancellationToken;
        var ok = await saver.SaveAsync(Block("spammer@example.org"), ct);
        Assert.True(ok.IsOk, ok.Error?.Message);
        var text = f.Server.Api.ActiveSieveText()!;
        Assert.Contains("# mine\nif header :contains \"subject\" \"x\" { discard; }", text, StringComparison.Ordinal);

        // a candidate the server cannot compile is not saved and says why
        var bad = Block("fake-invalid-sieve@example.org");
        var refused = await saver.SaveAsync(bad, ct);
        Assert.False(refused.IsOk);
        Assert.Contains("Not saved", refused.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(text, f.Server.Api.ActiveSieveText());
    }
}
