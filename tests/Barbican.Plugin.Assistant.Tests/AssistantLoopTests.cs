// Not Rust rows: B-AI-007 (at most 16 model turns, "Stopped after too many steps."; Rust agent.rs:493-531) and B-AI-009 (Ask mode asks before a write tool; a declined action goes back to the model). M6 stream set-data.

using static Barbican.Plugin.Assistant.Tests.AssistantTestFakes;

namespace Barbican.Plugin.Assistant.Tests;

public sealed class AssistantLoopTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_loop_stops_after_sixteen_model_turns()
    {
        var client = new ScriptedClient(CallsTool);
        var tools = new CountingTools(writes: false);
        var progress = new List<string>();
        var r = await AssistantLoop.RunAsync(client, tools, Config(AssistantActionMode.Automatic), "a", [], (_, _) => Task.FromResult(true), progress.Add, Ct);
        Assert.True(r.IsOk);
        Assert.Equal("Stopped after too many steps.", r.Value);
        Assert.Equal(AssistantLoop.MaxTurns, client.Calls);
        Assert.Equal(AssistantLoop.MaxTurns, tools.Runs);
        Assert.Equal("step 1", progress[0]);
    }

    [Fact]
    public async Task A_declined_write_tool_does_not_run_and_the_model_is_told()
    {
        var client = new ScriptedClient(n => n == 1 ? CallsTool(1) : new AssistantMessage("assistant", "All right, I left it.", null, null));
        var tools = new CountingTools(writes: true);
        var history = new List<AssistantMessage>();
        var asked = 0;
        var r = await AssistantLoop.RunAsync(client, tools, Config(AssistantActionMode.Ask), "a", history, (_, _) =>
        {
            asked++;
            return Task.FromResult(false);
        }, _ => { }, Ct);
        Assert.Equal("All right, I left it.", r.Value);
        Assert.Equal(1, asked);
        Assert.Equal(0, tools.Runs);
        Assert.Contains(history, m => string.Equals(m.Role, "tool", StringComparison.Ordinal) && string.Equals(m.Text, "The user declined this action. Do not retry it unless they ask.", StringComparison.Ordinal));
    }
    [Fact]
    public async Task Automatic_mode_still_confirms_delete_and_honors_refusal()
    {
        var client = new ScriptedClient(n => n == 1
            ? new AssistantMessage("assistant", "", System.Text.Json.Nodes.JsonNode.Parse("""[{"id":"c1","name":"delete_messages","arguments":{}}]"""), null)
            : new AssistantMessage("assistant", "Left it.", null, null));
        var tools = new CountingTools(writes: true, name: "delete_messages");
        var history = new List<AssistantMessage>();
        var asked = 0;
        var result = await AssistantLoop.RunAsync(client, tools, Config(AssistantActionMode.Automatic), "a", history, (_, _) =>
        {
            asked++;
            return Task.FromResult(false);
        }, _ => { }, Ct);
        Assert.True(result.IsOk);
        Assert.Equal(1, asked);
        Assert.Equal(0, tools.Runs);
        Assert.Contains(history, m => string.Equals(m.Text, AssistantLoop.Declined, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Automatic_send_confirmation_shows_recipients_subject_and_body()
    {
        var client = new ScriptedClient(n => n == 1
            ? new AssistantMessage("assistant", "", System.Text.Json.Nodes.JsonNode.Parse("""[{"id":"c1","name":"send_email","arguments":{"to":"erin@example.net","bcc":"sam@example.org","subject":"Hello","body":"Message text"}}]"""), null)
            : new AssistantMessage("assistant", "Not sent.", null, null));
        var tools = new CountingTools(writes: true, name: "send_email");
        string? shown = null;
        await AssistantLoop.RunAsync(client, tools, Config(AssistantActionMode.Automatic), "a", [], (_, text) =>
        {
            shown = text;
            return Task.FromResult(false);
        }, _ => { }, Ct);
        Assert.Contains("erin@example.net", shown, StringComparison.Ordinal);
        Assert.Contains("sam@example.org", shown, StringComparison.Ordinal);
        Assert.Contains("Hello", shown, StringComparison.Ordinal);
        Assert.Contains("Message text", shown, StringComparison.Ordinal);
        Assert.Equal(0, tools.Runs);
    }

}
