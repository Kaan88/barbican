// New tests (spec behaviours/extras.md B-SPAM-001..004, B-PLUG-003): the bundled spam-score plugin, loaded through the plugin host as the app
// loads it, gives Rust spam.rs's verdicts: the score sources in order, flag and action, the AI header that only raises, no row without spam
// headers, the detail text, and the clean row only with showCleanSpam; a disabled plugin is not called. Owner unified.

using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Native;

namespace Barbican.Core.Tests.Plugins;

public sealed class SpamScoreRulesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NativePlugins Plugins(bool showClean = false, params string[] disabled) => new(
        () => new NativePluginHostOptions
        {
            BundledRoot = Path.Combine(AppContext.BaseDirectory, "plugins"),
            Disabled = new HashSet<string>(disabled, StringComparer.Ordinal),
        },
        () => showClean);

    private static JsonObject Email(params (string Name, string Value)[] headers) =>
        new JsonObject { ["headers"] = new JsonArray([.. headers.Select(h => (JsonNode)new JsonObject { ["name"] = h.Name, ["value"] = h.Value })]) };

    private static async Task<(string Tone, string Value)?> RowAsync(NativePlugins plugins, params (string, string)[] headers)
    {
        var rows = await plugins.EmailDetailsAsync(Email(headers), Ct);
        return rows.Count == 0 ? null : (rows[0].Tone, rows[0].Value);
    }

    [Theory]
    [InlineData("X-Spam-Status", "No, score=3.20 required=5.0", "warning", "Score 3.20 / 5.00")]
    [InlineData("X-Spamd-Result", "default: False [1.50 / 15.00]", "success", "Score 1.50 / 15.00")]
    [InlineData("X-Spamd-Result", "default: True [16.00 / 15.00]", "danger", "Score 16.00 / 15.00")]
    [InlineData("X-Spam-Score", "2.5", "success", "Score 2.50")]
    public async Task The_score_and_threshold_come_from_the_spam_headers(string header, string value, string tone, string detail)
    {
        using var plugins = Plugins(showClean: true);
        var row = await RowAsync(plugins, (header, value));
        Assert.Equal(tone, row!.Value.Tone);
        Assert.Contains(detail, row.Value.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_flag_or_a_spam_action_is_spam_whatever_the_score()
    {
        using var plugins = Plugins();
        Assert.Equal("danger", (await RowAsync(plugins, ("X-Spam-Flag", "YES"), ("X-Spam-Score", "0.1")))!.Value.Tone);
        Assert.Equal("danger", (await RowAsync(plugins, ("X-Rspamd-Action", "add header")))!.Value.Tone);
    }

    [Fact]
    public async Task The_AI_header_raises_a_clean_verdict_and_never_lowers_one()
    {
        using var plugins = Plugins(showClean: true);
        Assert.Equal("warning", (await RowAsync(plugins, ("X-Spam-Score", "0.5"), ("X-Spam-LLM", "SUSPICIOUS: phishing wording")))!.Value.Tone);
        Assert.Equal("danger", (await RowAsync(plugins, ("X-Spam-Status", "Yes, score=9 required=5"), ("X-Spam-LLM", "LEGITIMATE")))!.Value.Tone);
    }

    [Fact]
    public async Task No_spam_headers_no_row_and_clean_mail_shows_only_with_show_clean()
    {
        using var hidden = Plugins(showClean: false);
        Assert.Null(await RowAsync(hidden, ("Subject", "hello")));
        Assert.Null(await RowAsync(hidden, ("X-Spam-Score", "0.3")));

        using var shown = Plugins(showClean: true);
        Assert.Equal("success", (await RowAsync(shown, ("X-Spam-Score", "0.3")))!.Value.Tone);
    }

    [Fact]
    public async Task A_disabled_plugin_is_not_called()
    {
        using var plugins = Plugins(showClean: true, "spam-score");
        Assert.Null(await RowAsync(plugins, ("X-Spam-Flag", "yes")));
        Assert.Contains(plugins.Plugins, p => string.Equals(p.Id, "spam-score", StringComparison.Ordinal) && !p.Enabled);
    }
}
