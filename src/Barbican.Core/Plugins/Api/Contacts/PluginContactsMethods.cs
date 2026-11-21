// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:178-186 (contacts:read / contacts:write per method) and :1019-1090 (the calls). Typed adapters over the contact store and the writers the Contacts view uses; no raw JMAP is exposed.
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.Contacts;

/// <summary>Factory for the contact and address-book adapters.</summary>
public static class PluginContactsMethods
{
    /// <summary>The number of methods <see cref="Create"/> returns (two address-book, six contact).</summary>
    public const int Count = 8;

    public static IReadOnlyList<IPluginApiMethod> Create(IPluginContactsHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return
        [
            new PluginAddressBookList(host), new PluginAddressBookCreate(host), new PluginContactList(host), new PluginContactGet(host),
            new PluginContactSearch(host), new PluginContactCreate(host), new PluginContactUpdate(host), new PluginContactDelete(host),
        ];
    }
}
