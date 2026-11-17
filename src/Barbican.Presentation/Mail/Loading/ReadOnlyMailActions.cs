// Mirrors: M1-plan §6.5 ("mark-as-read never sent in M1"); CLAUDE.md (<readonly-account> is read-only). Owner body.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>The M1 actions: nothing is ever written.</summary>
public sealed class ReadOnlyMailActions : IMailActions
{
    public bool CanWrite => false;

    public Task<Result<Unit>> MarkReadAsync(string emailId, CancellationToken ct) =>
        Task.FromResult(Result<Unit>.Fail(new ReadOnlyViolation("Email/set")));
}
