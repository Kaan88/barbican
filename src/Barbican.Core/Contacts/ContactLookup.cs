// Mirrors: see IContactLookup. Stub from the M5 plan section 4.5; owner con-data replaces the class (constructor dependencies: IContactStore, IAccountManager, IHookBus).

using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>The default <see cref="IContactLookup"/>.</summary>
public sealed class ContactLookup : IContactLookup
{
    public Task<IReadOnlyList<ContactMatch>> SuggestAsync(string text, int max, CancellationToken ct) => throw new PortPendingException("ContactLookup.SuggestAsync");

    public Task<ContactMatch?> FindByEmailAsync(string address, CancellationToken ct) => throw new PortPendingException("ContactLookup.FindByEmailAsync");
}
