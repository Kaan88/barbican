// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1997-2008 doAppGetInfo. The version is the Barbican.Core assembly's informational version
// (build metadata after "+" removed), never a path or machine detail.
using System.Reflection;

namespace Barbican.Core.Plugins.Api.App;

/// <summary>The production <see cref="IPluginAppInfo"/>.</summary>
public sealed class PluginAppInfo : IPluginAppInfo
{
    public string Name => "Barbican";

    public string Version
    {
        get
        {
            var text = typeof(PluginAppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            var plus = text.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? text[..plus] : text;
        }
    }
}
