// Session history is per account and clear serializes with in-flight questions (B-AI-011, B-SET-039).
using static Barbican.Plugin.Assistant.Tests.AssistantTestFakes;

namespace Barbican.Plugin.Assistant.Tests;

public sealed class AssistantSessionTests
{
    private static AssistantSession Session(IAssistantClient client, string root, Func<string, string, CancellationToken, Task<bool>>? confirm = null)
    {
        var store = new MemorySettings();
        return new AssistantSession(client, new CountingTools(false), new AssistantSettingsReader(store), store, root, TimeProvider.System, confirm);
    }

    [Fact]
    public async Task Clear_waits_for_question_then_removes_only_that_accounts_history()
    {
        var root = Path.Combine(Path.GetTempPath(), "assistant-session-" + Guid.NewGuid().ToString("N"));
        try
        {
            var client = new BlockingClient();
            var session = Session(client, root);
            var ask = session.AskAsync("account-a", "question", null, TestContext.Current.CancellationToken);
            await client.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            var clear = session.ClearAsync("account-a", TestContext.Current.CancellationToken);
            Assert.False(clear.IsCompleted);
            client.Release.TrySetResult();
            Assert.True((await ask).IsOk);
            await clear;
            Assert.True((await session.AskAsync("account-b", "other question", null, TestContext.Current.CancellationToken)).IsOk);
            Assert.Empty(session.History("account-a"));
            Assert.Equal(2, session.History("account-b").Count);
            Assert.Empty(Session(client, root).History("account-a"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Saved_history_loads_asynchronously_after_reopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "assistant-reopen-" + Guid.NewGuid().ToString("N"));
        try
        {
            var client = new ScriptedClient(_ => new AssistantMessage("assistant", "answer", null, null));
            var first = Session(client, root);
            Assert.True((await first.AskAsync("a", "question", null, TestContext.Current.CancellationToken)).IsOk);
            var reopened = Session(client, root);
            Assert.Empty(reopened.History("a"));
            await reopened.InitializeHistoryAsync("a", TestContext.Current.CancellationToken);
            Assert.Collection(reopened.History("a"), first => Assert.Equal("question", first.Text), second => Assert.Equal("answer", second.Text));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_failed_question_leaves_the_history_as_it_was()
    {
        var root = Path.Combine(Path.GetTempPath(), "assistant-fail-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = Session(new FailingClient(), root);
            var result = await session.AskAsync("a", "question", null, TestContext.Current.CancellationToken);
            Assert.False(result.IsOk);
            Assert.Equal("provider down", result.Error);
            Assert.Empty(session.History("a"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Writes_are_declined_when_the_host_offers_no_dialog()
    {
        var root = Path.Combine(Path.GetTempPath(), "assistant-nodialog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var tools = new CountingTools(writes: true);
            var store = new MemorySettings();
            var client = new ScriptedClient(n => n == 1 ? CallsTool(1) : new AssistantMessage("assistant", "Left it.", null, null));
            var session = new AssistantSession(client, tools, new AssistantSettingsReader(store), store, root, TimeProvider.System);
            Assert.Equal("Left it.", (await session.AskAsync("a", "do it", null, TestContext.Current.CancellationToken)).Value);
            Assert.Equal(0, tools.Runs);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task The_chat_shows_user_and_assistant_turns_only()
    {
        var root = Path.Combine(Path.GetTempPath(), "assistant-chat-" + Guid.NewGuid().ToString("N"));
        try
        {
            var client = new ScriptedClient(n => n == 1 ? CallsTool(1) : new AssistantMessage("assistant", "final", null, null));
            var store = new MemorySettings();
            var session = new AssistantSession(client, new CountingTools(false), new AssistantSettingsReader(store), store, root, TimeProvider.System);
            var chat = new AssistantChat(session);
            var reply = await chat.SendAsync("a", "question", null, TestContext.Current.CancellationToken);
            Assert.Null(reply.Error);
            Assert.Equal("final", reply.Text);
            Assert.Collection(chat.Turns("a"), t => Assert.True(t.FromUser), t => Assert.Equal("step 1", t.Text), t => Assert.Equal("final", t.Text));
            await chat.ClearAsync("a", TestContext.Current.CancellationToken);
            Assert.Empty(chat.Turns("a"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FailingClient : IAssistantClient
    {
        public Task<AssistantResult<AssistantMessage>> CompleteAsync(AssistantConfig config, IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantToolSpec> tools, CancellationToken ct) =>
            Task.FromResult(AssistantResult<AssistantMessage>.Fail("provider down"));
    }

    private sealed class BlockingClient : IAssistantClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AssistantResult<AssistantMessage>> CompleteAsync(AssistantConfig config, IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantToolSpec> tools, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return AssistantResult<AssistantMessage>.Ok(new AssistantMessage("assistant", "answer", null, null));
        }
    }
}
