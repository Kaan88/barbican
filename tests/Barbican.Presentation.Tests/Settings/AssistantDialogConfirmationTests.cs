using Barbican.Presentation.Actions;
using Barbican.Presentation.Common;
using Barbican.Presentation.Settings.Integration;

namespace Barbican.Presentation.Tests.Settings;

public sealed class AssistantDialogConfirmationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_tool_waits_for_the_dispatched_dialog_and_preserves_the_answer(bool answer)
    {
        var dispatcher = new TrackingDispatcher();
        var confirmer = new TrackingConfirmer(dispatcher, answer);
        var bridge = new AssistantDialogConfirmation(dispatcher, confirmer);
        Assert.Equal(answer, await bridge.ConfirmAsync("send_email", "Send the draft", CancellationToken.None));
        Assert.Equal(1, dispatcher.Invocations);
        Assert.True(confirmer.WasAsked);
    }

    [Fact]
    public async Task A_cancelled_request_never_opens_a_dialog()
    {
        var dispatcher = new TrackingDispatcher();
        var confirmer = new TrackingConfirmer(dispatcher, true);
        var bridge = new AssistantDialogConfirmation(dispatcher, confirmer);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bridge.ConfirmAsync("send_email", "Send", source.Token));
        Assert.False(confirmer.WasAsked);
        Assert.Equal(0, dispatcher.Invocations);
    }

    [Fact]
    public async Task Without_a_dialog_host_the_action_is_declined()
    {
        var dispatcher = new TrackingDispatcher();
        var bridge = new AssistantDialogConfirmation(dispatcher);
        Assert.False(await bridge.ConfirmAsync("delete_messages", "Delete", CancellationToken.None));
        Assert.Equal(0, dispatcher.Invocations);
    }

    private sealed class TrackingDispatcher : IDispatcher
    {
        public bool HasThreadAccess { get; private set; }
        public int Invocations { get; private set; }
        public void Post(Action action) => throw new NotSupportedException();
        public async Task InvokeAsync(Func<Task> action)
        {
            Invocations++;
            HasThreadAccess = true;
            try { await action(); }
            finally { HasThreadAccess = false; }
        }
    }

    private sealed class TrackingConfirmer(TrackingDispatcher dispatcher, bool answer) : IActionConfirmer
    {
        public bool WasAsked { get; private set; }
        public Task<bool> ConfirmAsync(ConfirmRequest request, CancellationToken ct)
        {
            Assert.True(dispatcher.HasThreadAccess);
            Assert.Equal("Send the draft", request.Message);
            WasAsked = true;
            return Task.FromResult(answer);
        }
    }
}
