// Ambiguous held cancellation must be reconciled rather than silently losing the sent notification.
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Tests.Compose;

public sealed class HeldSendReconciliationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FailedCancelAndFirstStatusReadEventuallyNotifyFinalExactlyOnce()
    {
        var hooks = new Observer();
        FailingClient? wrapper = null;
        var rig = await PipelineRig.StartAsync(addOns: hooks, wrapClient: client => wrapper = new FailingClient(client));
        var sent = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions { UndoSeconds = 20 }, Ct);
        Assert.True(sent.IsOk, sent.Error?.Message);
        wrapper!.FailUndo = true;
        var undo = await rig.Sender.UndoAsync(sent.Value!, Ct);
        Assert.False(undo.IsOk);
        Assert.Equal(1, wrapper.CancelFailures);
        Assert.Equal(1, wrapper.StatusFailures);
        Assert.Empty(hooks.Notifications);
        rig.Clock.Advance(TimeSpan.FromSeconds(20));
        await wrapper.SuccessfulStatus.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        await hooks.Notified.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        var notification = Assert.Single(hooks.Notifications);
        Assert.Equal(sent.Value!.SubmissionId, notification.SubmissionId, StringComparer.Ordinal);
        rig.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Single(hooks.Notifications);
    }

    private sealed class Observer : IOutgoingAddOnHooks, IOutgoingAddOnSend
    {
        public List<EmailSentPayload> Notifications { get; } = [];
        public TaskCompletionSource Notified { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IOutgoingAddOnSend Start(IAccountSession sendingSession) => this;
        public Task<bool> CheckAsync(OutgoingEmailPayload outgoing, CancellationToken ct) => Task.FromResult(true);
        public Task<OutgoingEmailPayload> TransformAsync(OutgoingEmailPayload outgoing, CancellationToken ct) => Task.FromResult(outgoing);
        public Task<bool> TryIssueAsync(Func<bool> issue, CancellationToken ct) => Task.FromResult(issue());
        public void NotifySent(EmailSentPayload payload) { Notifications.Add(payload); Notified.TrySetResult(); }
    }

    private sealed class FailingClient(IJmapClient inner) : IJmapClient
    {
        public bool FailUndo { get; set; }
        public int CancelFailures { get; private set; }
        public int StatusFailures { get; private set; }
        public TaskCompletionSource SuccessfulStatus { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public JmapSession Session => inner.Session;
        public bool IsReadOnly => inner.IsReadOnly;
        public async Task<Result<JmapResponse>> SendAsync(JmapRequest request, CancellationToken ct)
        {
            var first = (string?)request.MethodCalls[0][0];
            if (FailUndo && string.Equals(first, "EmailSubmission/set", StringComparison.Ordinal) && CancelFailures == 0)
            {
                CancelFailures++;
                return Result<JmapResponse>.Fail(new TransportError("Synthetic lost cancellation", Retryable: true));
            }
            if (FailUndo && string.Equals(first, "EmailSubmission/get", StringComparison.Ordinal) && StatusFailures == 0)
            {
                StatusFailures++;
                return Result<JmapResponse>.Fail(new TransportError("Synthetic unavailable status", Retryable: true));
            }
            var result = await inner.SendAsync(request, ct);
            if (FailUndo && string.Equals(first, "EmailSubmission/get", StringComparison.Ordinal) && result.IsOk) SuccessfulStatus.TrySetResult();
            return result;
        }
        public Task<Result<Stream>> DownloadAsync(string accountId, string blobId, string name, string type, CancellationToken ct) => inner.DownloadAsync(accountId, blobId, name, type, ct);
        public T? AccountCapability<T>(string accountId, string capability) where T : class => inner.AccountCapability<T>(accountId, capability);
    }
}
