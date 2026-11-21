// Not Rust rows: B-AI-011 (Rust agent.rs:556-573 to_json, from_json: a chat survives a restart). M6 stream set-data.

using System.Text.Json.Nodes;

namespace Barbican.Plugin.Assistant.Tests;

public sealed class AssistantHistoryTests
{
    [Fact]
    public void History_round_trips_and_a_bad_entry_is_skipped()
    {
        var calls = JsonNode.Parse("""[{"id": "c1", "name": "list_folders", "arguments": {}}]""");
        var messages = new[]
        {
            new AssistantMessage("user", "hi", null, null),
            new AssistantMessage("assistant", string.Empty, calls, null),
            new AssistantMessage("tool", "Inbox, Sent", null, "c1"),
        };
        var json = AssistantHistory.ToJson(messages);
        var back = AssistantHistory.FromJson(JsonNode.Parse(json.ToJsonString()));
        Assert.Equal(3, back.Count);
        Assert.Equal("c1", back[2].ToolCallId);
        Assert.Equal("list_folders", (string?)back[1].ToolCalls![0]!["name"]);
        Assert.Null(back[0].ToolCalls);

        var broken = AssistantHistory.FromJson(JsonNode.Parse("""[1, {"text": "no role"}, {"role": "user", "text": "ok"}]"""));
        Assert.Equal("ok", Assert.Single(broken).Text);
        Assert.Empty(AssistantHistory.FromJson(null));
    }
}
