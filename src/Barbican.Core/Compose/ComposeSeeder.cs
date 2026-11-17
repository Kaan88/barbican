// Mirrors: see IComposeSeeder. Contract stub from the M3 plan section 4.3; owner send-pipeline replaces this class (constructor and body).

using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>The default registration of <see cref="IComposeSeeder"/>: every call fails with <see cref="PortPendingException"/> until stream send-pipeline lands.</summary>
public sealed class ComposeSeeder : IComposeSeeder
{
    public Task<Result<ComposeSeed>> SeedAsync(ComposeRequest request, CancellationToken ct) => Task.FromException<Result<ComposeSeed>>(new PortPendingException("ComposeSeeder.SeedAsync"));
}
