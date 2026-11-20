// Regression: each mutation of a held send must use the captured account guard at HTTP issue.
using Barbican.Core.Compose;

namespace Barbican.Core.Tests.Compose;

public sealed class HeldSendAdmissionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RefusedAccountGuardPreventsCancellationResubmissionAndRestore()
    {
        var rig = await PipelineRig.StartAsync();
        var sent = await rig.Sender.SendAsync(PipelineRig.NewDraft(), new SendOptions { UndoSeconds = 20 }, Ct);
        Assert.True(sent.IsOk, sent.Error?.Message);
        var context = ComposeContext.For(rig.Accounts, PipelineRig.Account).Value!;
        var attempts = 0;
        Task<bool> DenyAsync(Func<bool> issue, CancellationToken ct) { attempts++; return Task.FromResult(false); }
        var canceled = await SubmissionOps.CancelAsync(context, sent.Value!.SubmissionId, Ct, DenyAsync);
        Assert.False(canceled.IsOk);
        var created = await SubmissionOps.CreateAsync(context, sent.Value.EmailId, "i1", "user@example.org", ["erin@example.net"], null, null, Ct, DenyAsync);
        Assert.False(created.IsOk);
        Assert.False(await SubmissionOps.RestoreDraftAsync(context, sent.Value.EmailId, Ct, DenyAsync));
        Assert.False((await SubmissionOps.DestroyEmailAsync(context, sent.Value.EmailId, Ct, DenyAsync)).IsOk);
        Assert.True(attempts >= 4);
        Assert.Single(rig.Api.Submissions);
        Assert.Equal("pending", Assert.Single(rig.Api.SubmissionStates).UndoStatus);
        Assert.NotNull(rig.Store.Email(sent.Value.EmailId));
    }
}
