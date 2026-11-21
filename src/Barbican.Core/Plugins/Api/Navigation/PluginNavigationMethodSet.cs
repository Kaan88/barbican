// Composition helper: one set of navigation adapters per host, registered method by method in the app's container.
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.Navigation;

/// <summary>The navigation adapters of one host.</summary>
public sealed class PluginNavigationMethodSet(PluginNavigationHost host)
{
    public IReadOnlyList<IPluginApiMethod> Methods { get; } = PluginNavigationMethods.Create(host);
}
