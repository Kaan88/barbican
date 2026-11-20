#pragma warning disable MA0048, MA0006 // small fakes grouped in one file; ordinal string compares by nature
// Fakes of the send pipeline services for the view-model tests (no network): they record what the view models ask for.

using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Jmap.Errors;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Compose;

namespace Barbican.Presentation.Tests.Compose.Pipeline;

internal sealed class RecordingToasts : IToastService
{
    public List<ToastRequest> Shown { get; } = [];

    public ToastHandle Show(ToastRequest toast)
    {
        Shown.Add(toast);
        return default;
    }

    public void Dismiss(ToastHandle handle)
    {
    }
}

internal sealed class FakeComposer : IComposerService
{
    public List<ComposeRequest> Opened { get; } = [];

    public List<ComposeSeed> Seeds { get; } = [];

    public bool IsOpen => false;

    public event EventHandler? OpenChanged
    {
        add { }
        remove { }
    }

    public Task<ComposerViewModel?> OpenAsync(ComposeRequest request, CancellationToken ct)
    {
        Opened.Add(request);
        return Task.FromResult<ComposerViewModel?>(null);
    }

    public Task OpenSeedAsync(ComposeSeed seed, CancellationToken ct)
    {
        Seeds.Add(seed);
        return Task.CompletedTask;
    }

    public Task<bool> TryCloseAsync(CancellationToken ct) => Task.FromResult(true);
}

internal sealed class FakeIdentities(params string[] emails) : IIdentityService
{
    public Task<Result<IReadOnlyList<ComposeIdentity>>> ListAsync(string accountId, CancellationToken ct) =>
        Task.FromResult(Result<IReadOnlyList<ComposeIdentity>>.Ok([.. emails.Select((e, i) => new ComposeIdentity(new Barbican.Core.Mail.Model.Identity("i" + i, null, e), "", ""))]));
}

internal sealed class FakeSender : IMailSender
{
    public List<(ComposeDraft Draft, SendOptions Options)> Sent { get; } = [];

    public List<SendReceipt> Undone { get; } = [];

    public List<SendReceipt> Released { get; } = [];

    public SendReceipt Receipt { get; set; } = new("acct", "e1", "s1");

    public string? Failure { get; set; }

    public Task<Result<SendReceipt>> SendAsync(ComposeDraft draft, SendOptions options, CancellationToken ct)
    {
        Sent.Add((draft, options));
        return Task.FromResult(Failure is null ? Result<SendReceipt>.Ok(Receipt) : Result<SendReceipt>.Fail(new ValidationError(Failure)));
    }

    public Task<Result<ComposeSeed>> UndoAsync(SendReceipt receipt, CancellationToken ct)
    {
        Undone.Add(receipt);
        return Task.FromResult(Result<ComposeSeed>.Ok(new ComposeSeed { AccountId = receipt.AccountId, Body = new Barbican.Core.Editor.Doc(), DraftId = receipt.EmailId }));
    }

    public Task<Result<Unit>> SendNowAsync(SendReceipt receipt, CancellationToken ct)
    {
        Released.Add(receipt);
        return Task.FromResult(Result<Unit>.Ok(default));
    }
}

internal sealed class FakeScheduled : IScheduledSends
{
    public List<ScheduledMessage> Held { get; } = [];

    public List<(string Submission, DateTimeOffset At)> Rescheduled { get; } = [];

    public List<string> Cancelled { get; } = [];

    public long MaxDelayedSend(string accountId) => 7 * 86400;

    public Task<Result<IReadOnlyList<ScheduledMessage>>> ListAsync(string accountId, CancellationToken ct) =>
        Task.FromResult(Result<IReadOnlyList<ScheduledMessage>>.Ok([.. Held]));

    public Task<Result<Unit>> RescheduleAsync(string accountId, string submissionId, DateTimeOffset at, CancellationToken ct)
    {
        Rescheduled.Add((submissionId, at));
        return Task.FromResult(Result<Unit>.Ok(default));
    }

    public Task<Result<string>> CancelAsync(string accountId, string submissionId, CancellationToken ct)
    {
        Cancelled.Add(submissionId);
        return Task.FromResult(Result<string>.Ok(Held.First(h => h.SubmissionId == submissionId).EmailId));
    }
}
