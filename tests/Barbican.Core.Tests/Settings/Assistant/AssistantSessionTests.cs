// Session history is per account and clear serializes with in-flight questions.
using Barbican.Core.Common;
using Barbican.Core.Settings.Assistant;
using Barbican.Core.Storage;
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

    [Fact]
    public async Task Saved_history_loads_asynchronously_after_reopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "assistant-reopen-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new TestProfilePaths(root);
            using var settings = new AssistantSettings(paths);
            var client = new ScriptedClient(_ => new AssistantMessage("assistant", "answer", null, null));
            var first = new AssistantSession(client, new CountingTools(false), settings, ContactRig.Create(), paths, TimeProvider.System);
            Assert.True((await first.AskAsync("a", "question", null, TestContext.Current.CancellationToken)).IsOk);
            var reopened = new AssistantSession(client, new CountingTools(false), settings, ContactRig.Create(), paths, TimeProvider.System);
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
    public async Task History_does_not_wait_for_slow_disk_load()
    {
        using var paths = new BlockingPaths();
        using var settings = new AssistantSettings(paths);
        var session = new AssistantSession(new ScriptedClient(_ => new AssistantMessage("assistant", "answer", null, null)), new CountingTools(false), settings, ContactRig.Create(), paths, TimeProvider.System);
        var loading = session.InitializeHistoryAsync("a", TestContext.Current.CancellationToken);
        try
        {
            await paths.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var read = Task.Run(() => session.History("a"), TestContext.Current.CancellationToken);
            Assert.Empty(await read.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally
        {
            paths.Release.Set();
        }

        await loading;
    }

    private sealed class BlockingPaths : IProfilePaths, IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public string Profile => "test";
        public string ConfigDir => throw new NotSupportedException();
        public string DataDir
        {
            get
            {
                Entered.TrySetResult();
                Release.Wait();
                return Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N"));
            }
        }

        public void Dispose() => Release.Dispose();
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
