// Host-added M6b profile ownership; Bulwark stores/plugin-store.ts:188-320 assumes a single browser authority.
namespace Barbican.Core.Plugins.Persistence;

/// <summary>Exclusive host profile authority; lifetime stays readable and cancels on release.</summary>
public interface IPluginProfileAuthorityLease : IAsyncDisposable
{
    string Profile { get; }
    bool IsHeld { get; }
    CancellationToken Lifetime { get; }
}
