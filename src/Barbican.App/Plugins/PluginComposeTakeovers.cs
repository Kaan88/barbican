#if WINDOWS
// Mirrors Bulwark lib/outgoing/compose-takeover.ts and classification-raw.ts.
using Barbican.Core.Plugins.Api.RawSend;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.App.Plugins;

internal sealed class PluginComposeTakeovers
{
    private readonly Lock _gate = new();
    private readonly Dictionary<PluginRuntimeBinding, PluginComposeTakeover> _active = [];

    internal bool TryOpen(PluginComposeTakeover scope)
    {
        lock (_gate) return _active.TryAdd(scope.Binding, scope);
    }

    internal bool TryAuthorize(PluginRuntimeBinding binding, PluginRawSendRequest request, out PluginComposeTakeover? scope)
    {
        lock (_gate)
        {
            if (!_active.TryGetValue(binding, out scope)) return request.LabelToken is null;
            return request.LabelToken is not null && scope.TryClaim(request);
        }
    }

    internal void Close(PluginComposeTakeover scope)
    {
        lock (_gate)
        {
            scope.Close();
            _active.Remove(scope.Binding);
        }
    }
}
#endif
