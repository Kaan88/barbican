using System.Text.Json.Nodes;
using Barbican.Core.Compose;

namespace Barbican.Core.Tests.Compose;

/// <summary>Rust src/templates.rs test tombstone_keeps_other_deletions, ported (M3 stream send-pipeline).</summary>
public sealed class TemplateTombstoneTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    [Red("send-pipeline")]
    [RustTest("templates::tombstone_keeps_other_deletions")]
    public void Tombstone_keeps_other_deletions()
    {
        var m = TemplateTombstone.Mark(JsonNode.Parse("""{"version": 1, "deleted": {"a": "2026-01-01T00:00:00.000Z"}}"""), "b", Now);
        Assert.Equal("2026-01-01T00:00:00.000Z", (string?)m["deleted"]!["a"]);
        Assert.Equal("2026-11-17T08:00:00.000Z", (string?)m["deleted"]!["b"]);

        m = TemplateTombstone.Mark(null, "x", Now);
        Assert.Equal(1, (int?)m["version"]);
        Assert.Equal("2026-11-17T08:00:00.000Z", (string?)m["deleted"]!["x"]);
    }
}
