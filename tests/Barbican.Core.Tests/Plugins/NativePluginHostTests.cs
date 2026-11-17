// Ported from Rust src/plugins.rs test loads_spam_score_plugin (:130-:145). D-234: the Rust test loaded barbican_spam_score.dll through the C ABI and skipped when it
// was not built. This test loads the spam-score native .NET plugin through the plugin host from the test output (never skipped; Core.Tests copies the bundled build to
// <output>/plugins/spam-score/) and keeps the other assertions.

using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Native;

namespace Barbican.Core.Tests.Plugins;

public sealed class NativePluginHostTests
{
    [Fact]
    [RustTest("plugins::loads_spam_score_plugin")]
    public async Task Loads_spam_score_plugin()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "plugins");
        var loaded = NativePluginHost.LoadDirectory(root);
        Assert.True(loaded.IsOk, loaded.Error?.Message);
        var host = loaded.Value!;
        var p = Assert.Single(host.Plugins, x => string.Equals(x.Id, "spam-score", StringComparison.Ordinal));
        Assert.True(p.Handles("email.details"));

        var email = JsonNode.Parse("""{"headers": [{"name": "X-Spam-Status", "value": "Yes, score=7.5 required=5.0"}]}""")!;
        var rows = await host.EmailDetailsAsync(email, TestContext.Current.CancellationToken);
        var row = Assert.Single(rows);
        Assert.Equal("danger", row.Tone);
        Assert.Contains("7.50 / 5.00", row.Value, StringComparison.Ordinal);
    }
}
