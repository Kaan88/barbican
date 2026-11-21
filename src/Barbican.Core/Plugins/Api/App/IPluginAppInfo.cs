// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1997-2008 doAppGetInfo (name and version the app reports about itself).
namespace Barbican.Core.Plugins.Api.App;

/// <summary>What the app tells an add-on about itself: nothing about the user.</summary>
public interface IPluginAppInfo
{
    string Name { get; }

    string Version { get; }
}
