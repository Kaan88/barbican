// Mirrors: see IIdentityService. Contract stub from the M3 plan section 4.3; owner send-pipeline replaces this class (constructor and body).

using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>The default registration of <see cref="IIdentityService"/>: every call fails with <see cref="PortPendingException"/> until stream send-pipeline lands.</summary>
public sealed class IdentityService : IIdentityService
{
    public Task<Result<IReadOnlyList<ComposeIdentity>>> ListAsync(string accountId, CancellationToken ct) => Task.FromException<Result<IReadOnlyList<ComposeIdentity>>>(new PortPendingException("IdentityService.ListAsync"));
}
