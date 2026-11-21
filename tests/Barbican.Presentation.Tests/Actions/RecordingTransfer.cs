// A transfer double for the copy-to-account action (G-048): fixed picker entries, recorded copies, a chosen count or failure.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Actions;

namespace Barbican.Presentation.Tests.Actions;

internal sealed class RecordingTransfer : IMailTransfer
{
    public IReadOnlyList<CopyTargetAccount> Offered { get; set; } = [];

    public int Count { get; set; } = 1;

    public string? Failure { get; set; }

    public string? AskedSource { get; private set; }

    public List<(IReadOnlyList<MailTarget> Targets, string AccountKey, string MailboxId)> Copies { get; } = [];

    public Task<Result<IReadOnlyList<CopyTargetAccount>>> TargetsAsync(string? sourceAccountKey, CancellationToken ct)
    {
        AskedSource = sourceAccountKey;
        return Task.FromResult(Result<IReadOnlyList<CopyTargetAccount>>.Ok(Offered));
    }

    public Task<Result<int>> CopyAsync(IReadOnlyList<MailTarget> targets, string destAccountKey, string destMailboxId, CancellationToken ct)
    {
        Copies.Add((targets, destAccountKey, destMailboxId));
        return Task.FromResult(Failure is { } failure ? Result<int>.Fail(new ValidationError(failure)) : Result<int>.Ok(Count));
    }
}
