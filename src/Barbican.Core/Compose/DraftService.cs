// Mirrors: see IDraftService. Contract stub from the M3 plan section 4.3; owner send-pipeline replaces this class (constructor and body).

using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>The default registration of <see cref="IDraftService"/>: every call fails with <see cref="PortPendingException"/> until stream send-pipeline lands.</summary>
public sealed class DraftService : IDraftService
{
    public Task<Result<SavedDraft>> SaveAsync(ComposeDraft draft, CancellationToken ct) => Task.FromException<Result<SavedDraft>>(new PortPendingException("DraftService.SaveAsync"));

    public Task<Result<Unit>> DiscardAsync(string accountId, string draftId, CancellationToken ct) => Task.FromException<Result<Unit>>(new PortPendingException("DraftService.DiscardAsync"));
}
