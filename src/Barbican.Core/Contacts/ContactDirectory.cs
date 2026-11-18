// Mirrors: see IContactDirectory. Stub from the M5 plan section 4.4; owner con-data replaces the class (constructor dependency: IAccountManager).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>The default <see cref="IContactDirectory"/>.</summary>
public sealed class ContactDirectory : IContactDirectory
{
    public bool IsAvailable(string accountKey) => throw new PortPendingException("ContactDirectory.IsAvailable");

    public Task<Result<(IReadOnlyList<JsonNode> Principals, bool Fallback)>> LoadAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("ContactDirectory.LoadAsync");
}
