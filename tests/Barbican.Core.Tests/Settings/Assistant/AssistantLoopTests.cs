// Not Rust rows: B-AI-007 (at most 16 model turns, "Stopped after too many steps."; Rust agent.rs:493-531) and B-AI-009 (Ask mode asks before a write tool; a declined action goes back to the model). M6 stream set-data.

using Barbican.Core.Settings.Assistant;
using static Barbican.Core.Tests.Settings.Assistant.AssistantTestFakes;

namespace Barbican.Core.Tests.Settings.Assistant;

public sealed class AssistantLoopTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Red("set-data")]
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
    [Red("set-data")]
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
}
