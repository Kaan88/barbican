// Session history is per account and clear serializes with in-flight questions.
using Barbican.Core.Common;
using Barbican.Core.Settings.Assistant;
using Barbican.Core.Tests.Contacts;
using Barbican.Core.Tests.Settings.Prefs;
using static Barbican.Core.Tests.Settings.Assistant.AssistantTestFakes;

namespace Barbican.Core.Tests.Settings.Assistant;

public sealed class AssistantSessionTests
{
    [Fact]
    public async Task Clear_waits_for_question_then_removes_only_that_accounts_history()
    {
        var root = Path.Combine(Path.GetTempPath(), "assistant-session-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new TestProfilePaths(root);
            var client = new BlockingClient();
            var session = new AssistantSession(client, new CountingTools(false), new AssistantSettings(paths), ContactRig.Create(), paths, TimeProvider.System);
            var ask = session.AskAsync("account-a", "question", null, TestContext.Current.CancellationToken);
            await client.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            var clear = session.ClearAsync("account-a", TestContext.Current.CancellationToken);
            Assert.False(clear.IsCompleted);
            client.Release.TrySetResult();
            Assert.True((await ask).IsOk);
            Assert.True((await clear).IsOk);
            Assert.True((await session.AskAsync("account-b", "other question", null, TestContext.Current.CancellationToken)).IsOk);
            Assert.Empty(session.History("account-a"));
            Assert.Equal(2, session.History("account-b").Count);
            Assert.Empty(new AssistantSession(client, new CountingTools(false), new AssistantSettings(paths), ContactRig.Create(), paths, TimeProvider.System).History("account-a"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class BlockingClient : IAssistantClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<Result<AssistantMessage>> CompleteAsync(AssistantConfig config, IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantToolSpec> tools, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return Result<AssistantMessage>.Ok(new AssistantMessage("assistant", "answer", null, null));
        }
    }
}
