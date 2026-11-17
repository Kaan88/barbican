// Test double for IMailActions that may write (the M2 shape), so the markAsReadDelay logic can be checked. Owner body.

using System.Collections.Concurrent;
using Barbican.Core.Common;
using Barbican.Presentation.Mail.Loading;

namespace Barbican.Presentation.Tests.Mail.Loading;

internal sealed class FakeMailActions(bool canWrite) : IMailActions
{
    public bool CanWrite => canWrite;

    public ConcurrentQueue<string> MarkedRead { get; } = new();

    public Task<Result<Unit>> MarkReadAsync(string emailId, CancellationToken ct)
    {
        MarkedRead.Enqueue(emailId);
        return Task.FromResult(Result<Unit>.Ok(default));
    }
}
