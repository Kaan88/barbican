// Mirrors: see IContactStore. Stub from the M5 plan section 4.2; owner con-data replaces the class (constructor dependencies: IAccountManager, IHookBus, TimeProvider; the push engine of Core/Sync raises RefreshAsync).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>The default <see cref="IContactStore"/>.</summary>
public sealed class ContactStore : IContactStore
{
    public event EventHandler<ContactChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public Task<Result<ContactSnapshot>> LoadAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("ContactStore.LoadAsync");

    public ContactSnapshot? Snapshot(string accountKey) => throw new PortPendingException("ContactStore.Snapshot");

    public Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("ContactStore.RefreshAsync");

    public JsonNode? Patch(string accountKey, string cardId, JsonObject patch) => throw new PortPendingException("ContactStore.Patch");

    public void Restore(string accountKey, string cardId, JsonNode? previous) => throw new PortPendingException("ContactStore.Restore");
}
